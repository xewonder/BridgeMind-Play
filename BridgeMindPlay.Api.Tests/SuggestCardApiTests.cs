using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BridgeMindPlay.Api.Tests
{
    [TestClass]
    public class SuggestCardApiTests
    {
        private static WebApplication _app = null!;
        private static HttpClient _client = null!;

        [ClassInitialize]
        public static void StartApp(TestContext _)
        {
            _app = Program.BuildApp(bindUrlOverride: "http://127.0.0.1:0");
            _app.StartAsync().GetAwaiter().GetResult();
            _client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
        }

        [ClassCleanup]
        public static void StopApp()
        {
            _client.Dispose();
            _app.StopAsync().GetAwaiter().GetResult();
            _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        //  1
        [TestMethod]
        public async Task Health_ReturnsOk()
        {
            var response = await _client.GetAsync("/health");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("{\"status\":\"ok\"}", await response.Content.ReadAsStringAsync());
        }

        //  2 + 3
        [TestMethod]
        public async Task ValidMultiCardRequest_Returns200_AndCardIsLegal()
        {
            var request = Fixture.ValidRequest();
            var response = await PostJson(request);
            var body = await response.Content.ReadAsStringAsync();

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"expected 200, got {response.StatusCode}: {body}");

            var (suit, rank) = ReadCard(body);
            var legal = ((List<Dictionary<string, object?>>)request["legalCards"]!).Select(c => ((int)c["rank"]!, (int)c["suit"]!)).ToHashSet();

            CollectionAssert.Contains(legal.ToList(), (rank, suit), $"suggested {suit}/{rank} is not among legalCards");
        }

        //  4
        [TestMethod]
        public async Task SingleLegalCard_FastPath_ReturnsThatExactCard()
        {
            var request = Fixture.ValidRequest();
            request["legalCards"] = Fixture.LegalCards("5S");

            var response = await PostJson(request);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("{\"suit\":3,\"rank\":5}", await response.Content.ReadAsStringAsync());
        }

        //  5
        [TestMethod]
        public async Task MissingLegalCards_Returns400()
        {
            var request = Fixture.ValidRequest();
            request.Remove("legalCards");
            await AssertBadRequest(request);
        }

        //  6
        [TestMethod]
        public async Task EmptyLegalCards_Returns400()
        {
            var request = Fixture.ValidRequest();
            request["legalCards"] = new List<object>();
            await AssertBadRequest(request);
        }

        //  7
        [TestMethod]
        public async Task NullTrick_Returns400()
        {
            var request = Fixture.ValidRequest();
            request["trick"] = null;
            await AssertBadRequest(request);
        }

        //  8
        [TestMethod]
        public async Task NullCardsPlayed_Returns400()
        {
            var request = Fixture.ValidRequest();
            request["cardsPlayed"] = null;
            await AssertBadRequest(request);
        }

        //  9
        [TestMethod]
        public async Task NullPlayers_Returns400()
        {
            var request = Fixture.ValidRequest();
            request["players"] = null;
            await AssertBadRequest(request);
        }

        //  10
        [TestMethod]
        public async Task InvalidRank_Returns400()
        {
            var request = Fixture.ValidRequest();
            ((List<Dictionary<string, object?>>)request["legalCards"]!).Add(new Dictionary<string, object?> { ["rank"] = 15, ["suit"] = 3 });
            await AssertBadRequest(request);
        }

        //  11
        [TestMethod]
        public async Task InvalidSuit_Returns400()
        {
            var request = Fixture.ValidRequest();
            ((List<Dictionary<string, object?>>)request["legalCards"]!).Add(new Dictionary<string, object?> { ["rank"] = 10, ["suit"] = 5 });
            await AssertBadRequest(request);
        }

        //  12
        [TestMethod]
        public async Task MalformedJson_Returns400()
        {
            var response = await _client.PostAsync("/suggest-card", new StringContent("{ this is not json", Encoding.UTF8, "application/json"));
            await AssertErrorShape(response, HttpStatusCode.BadRequest);
        }

        //  13
        [TestMethod]
        public async Task WrongContentType_Returns400()
        {
            var response = await _client.PostAsync("/suggest-card",
                new StringContent(Fixture.ToJson(Fixture.ValidRequest()), Encoding.UTF8, "text/plain"));
            await AssertErrorShape(response, HttpStatusCode.BadRequest);
        }

        //  14
        [TestMethod]
        public async Task GetOnSuggestCard_Returns405()
        {
            var response = await _client.GetAsync("/suggest-card");
            Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }

        //  15 (400 bodies)
        [TestMethod]
        public async Task ValidationErrors_DoNotLeakInternals()
        {
            var requests = new List<Dictionary<string, object?>>();

            var r1 = Fixture.ValidRequest();
            r1.Remove("legalCards");
            requests.Add(r1);

            var r2 = Fixture.ValidRequest();
            ((List<Dictionary<string, object?>>)r2["legalCards"]!).Add(new Dictionary<string, object?> { ["rank"] = 0, ["suit"] = 0 });
            requests.Add(r2);

            var r3 = Fixture.ValidRequest();
            r3["legalCards"] = new List<object> { new Dictionary<string, object?> { ["r"] = 14, ["s"] = 4 } };
            requests.Add(r3);

            foreach (var request in requests)
            {
                var response = await PostJson(request);
                await AssertErrorShape(response, HttpStatusCode.BadRequest);
            }
        }

        //  15 (500 body): state passes validation but the engine itself throws during SortCardMembers
        //  (upstream limitation: Trickster SuggestSorter cannot parse space-separated hand notation, only concatenated)
        [TestMethod]
        public async Task EngineFailureOnValidRequest_ReturnsGeneric500_WithoutLeak()
        {
            var request = Fixture.ValidRequest();
            var players = (List<Dictionary<string, object?>>)request["players"]!;
            players[0]["hand"] = string.Join(" ", Fixture.ActorHandTokens); // valid to the eye, fatal to the sorter
            request["player"] = players[0];

            var response = await PostJson(request);
            await AssertErrorShape(response, HttpStatusCode.InternalServerError);
            Assert.AreEqual("{\"error\":\"Internal server error.\"}", await response.Content.ReadAsStringAsync());
        }

        //  a state with no declarer side at all is tolerated by the engine; it must still answer with a legal card
        [TestMethod]
        public async Task AllDefendersMidTrick_StillReturnsLegalCard()
        {
            var request = Fixture.ValidRequest();
            var players = (List<Dictionary<string, object?>>)request["players"]!;
            foreach (var player in players)
                player["bid"] = 401;

            request["trick"] = new List<Dictionary<string, object?>>
            {
                Fixture.Card("AH"), Fixture.Card("KH"), Fixture.Card("5H"), Fixture.Card("2H"),
            };
            request["cardsPlayed"] = request["trick"];
            request["cardsPlayedInOrder"] = "0AH1KH25H32H";
            request["cardTakingTrick"] = Fixture.Card("AH");
            request["isPartnerTakingTrick"] = true;
            request["legalCards"] = Fixture.LegalCards("KC", "QC", "9C", "JH", "8H", "7H", "AD", "KD", "3D", "6S", "2S");

            var response = await PostJson(request);
            var body = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);

            var (suit, rank) = ReadCard(body);
            var legal = ((List<Dictionary<string, object?>>)request["legalCards"]!).Select(c => ((int)c["rank"]!, (int)c["suit"]!)).ToHashSet();
            CollectionAssert.Contains(legal.ToList(), (rank, suit), $"suggested {suit}/{rank} is not among legalCards");
        }

        //  bonus: duplicate legal cards are rejected
        [TestMethod]
        public async Task DuplicateLegalCards_Returns400()
        {
            var request = Fixture.ValidRequest();
            ((List<Dictionary<string, object?>>)request["legalCards"]!).Add(Fixture.Card("KC"));
            await AssertBadRequest(request);
        }

        //  bonus: the Trickster "r"/"s" shorthand is NOT accepted silently
        [TestMethod]
        public async Task ShorthandCardKeys_Returns400()
        {
            var request = Fixture.ValidRequest();
            request["legalCards"] = new List<object> { new Dictionary<string, object?> { ["r"] = 14, ["s"] = 4 } };
            await AssertBadRequest(request);
        }

        //  bonus: player must correspond to one of players
        [TestMethod]
        public async Task PlayerNotInPlayers_Returns400()
        {
            var request = Fixture.ValidRequest();
            request["player"] = Fixture.Player(4, 401, "", new List<int>());
            await AssertBadRequest(request);
        }

        private static async Task<HttpResponseMessage> PostJson(Dictionary<string, object?> request)
        {
            var content = new StringContent(Fixture.ToJson(request), Encoding.UTF8, "application/json");
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return await _client.PostAsync("/suggest-card", content);
        }

        private static async Task AssertBadRequest(Dictionary<string, object?> request)
        {
            var response = await PostJson(request);
            await AssertErrorShape(response, HttpStatusCode.BadRequest);
        }

        private static async Task AssertErrorShape(HttpResponseMessage response, HttpStatusCode expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(expected, response.StatusCode, $"expected {expected}, got {response.StatusCode}: {body}");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            Assert.AreEqual(JsonValueKind.Object, root.ValueKind, body);
            Assert.AreEqual(1, root.EnumerateObject().Count(), $"error responses must carry only {{\"error\": ...}}: {body}");
            Assert.AreEqual(JsonValueKind.String, root.GetProperty("error").ValueKind, body);

            foreach (var marker in new[] { "Stack", "stack", "Exception", "Trickster", "BridgeBot", "System.", "at Microsoft", ".dll", ".cs", "\\\\" })
                Assert.IsFalse(body.Contains(marker, StringComparison.Ordinal), $"response leaks internals (marker '{marker}'): {body}");
        }

        private static (int suit, int rank) ReadCard(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return (doc.RootElement.GetProperty("suit").GetInt32(), doc.RootElement.GetProperty("rank").GetInt32());
        }
    }
}
