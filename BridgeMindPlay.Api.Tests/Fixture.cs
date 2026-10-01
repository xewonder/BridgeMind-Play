using System.Text.Json;

namespace BridgeMindPlay.Api.Tests
{
    //  Builds valid Bridge suggest-card requests shaped exactly like the (adapted) TestBots Bridge
    //  opening-lead fixture: 4 players rotated so the player to act is first, declarer carries the
    //  encoded contract bid (4S = 439), dummy = -5, defenders = 401, unknown hands as "0U" tokens.
    internal static class Fixture
    {
        //  seat 2 (West, a defender) opens the play against 4S by seat 1 (North); seats rotated [2,3,0,1]
        public static readonly string[] ActorHandTokens =
        {
            "KC", "QC", "9C", "JH", "8H", "7H", "5H", "AD", "KD", "3D", "6S", "5S", "2S",
        };

        private const int Contract4S = 439; // 404 + suit 3 + (4 << 3)
        private const int Dummy = -5;
        private const int Defend = 401;
        private const int Pass = -2;
        private const string UnknownHand = "0U0U0U0U0U0U0U0U0U0U0U0U0U"; // 13 unknown cards

        public static Dictionary<string, object?> Card(string token) => new()
        {
            ["rank"] = TokenRank(token[0]),
            ["suit"] = TokenSuit(token[1]),
        };

        public static List<Dictionary<string, object?>> LegalCards(params string[] tokens) => tokens.Select(Card).ToList();

        public static Dictionary<string, object?> Player(int seat, int bid, string hand, List<int> bidHistory) => new()
        {
            ["seat"] = seat,
            ["bid"] = bid,
            ["bidHistory"] = bidHistory,
            ["hand"] = hand,
            ["cardsTaken"] = "",
            ["gameScore"] = 0,
            ["handScore"] = 0,
            ["folded"] = false,
            ["goodSuit"] = 0,
            ["playedCards"] = new List<object>(),
            ["voidSuits"] = new List<object>(),
        };

        public static Dictionary<string, object?> ValidRequest()
        {
            var actor = Player(2, Defend, string.Concat(ActorHandTokens), new List<int> { Pass });
            var players = new List<Dictionary<string, object?>>
            {
                actor,
                Player(3, Dummy, UnknownHand, new List<int> { Pass }),
                Player(0, Defend, UnknownHand, new List<int> { Pass }),
                Player(1, Contract4S, UnknownHand, new List<int> { Contract4S }),
            };

            return new Dictionary<string, object?>
            {
                ["options"] = new Dictionary<string, object?> { ["variation"] = 1, ["bidding"] = 0 },
                ["trumpSuit"] = 3,
                ["player"] = actor,
                ["players"] = players,
                ["legalCards"] = LegalCards(ActorHandTokens),
                ["trick"] = new List<object>(),
                ["cardsPlayed"] = new List<object>(),
                ["cardsPlayedInOrder"] = "",
                ["cardTakingTrick"] = null,
                ["isPartnerTakingTrick"] = false,
                ["trickTaker"] = null,
                ["cloudCard"] = null,
            };
        }

        public static string ToJson(Dictionary<string, object?> request) => JsonSerializer.Serialize(request);

        private static int TokenRank(char c) => c switch
        {
            'A' => 14, 'K' => 13, 'Q' => 12, 'J' => 11, 'T' => 10, _ => c - '0',
        };

        private static int TokenSuit(char c) => c switch
        {
            'C' => 1, 'D' => 2, 'S' => 3, 'H' => 4, _ => throw new ArgumentException($"bad suit letter {c}"),
        };
    }
}
