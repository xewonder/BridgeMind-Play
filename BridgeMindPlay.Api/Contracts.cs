using System.Text.Json.Serialization;
using Trickster.cloud;

namespace BridgeMindPlay.Api
{
    //  explicit BridgeMind request shape for SuggestCardState<BridgeOptions>.
    //  cards use full "suit"/"rank" numeric members only; the Trickster "r"/"s" shorthand is NOT accepted.
    public sealed class SuggestCardRequest
    {
        [JsonPropertyName("options")]
        public BridgeOptions? Options { get; set; }

        [JsonPropertyName("trumpSuit")]
        public int? TrumpSuit { get; set; }

        [JsonPropertyName("player")]
        public PlayerRequest? Player { get; set; }

        [JsonPropertyName("players")]
        public List<PlayerRequest>? Players { get; set; }

        [JsonPropertyName("legalCards")]
        public List<CardRequest>? LegalCards { get; set; }

        [JsonPropertyName("trick")]
        public List<CardRequest>? Trick { get; set; }

        [JsonPropertyName("cardsPlayed")]
        public List<CardRequest>? CardsPlayed { get; set; }

        [JsonPropertyName("cardsPlayedInOrder")]
        public string? CardsPlayedInOrder { get; set; }

        [JsonPropertyName("cardTakingTrick")]
        public CardRequest? CardTakingTrick { get; set; }

        [JsonPropertyName("isPartnerTakingTrick")]
        public bool IsPartnerTakingTrick { get; set; }

        [JsonPropertyName("trickTaker")]
        public PlayerRequest? TrickTaker { get; set; }

        [JsonPropertyName("cloudCard")]
        public CardRequest? CloudCard { get; set; }
    }

    public sealed class CardRequest
    {
        [JsonPropertyName("rank")]
        public int? Rank { get; set; }

        [JsonPropertyName("suit")]
        public int? Suit { get; set; }

        [JsonPropertyName("isTrump")]
        public bool IsTrump { get; set; }
    }

    public sealed class PlayedCardRequest
    {
        [JsonPropertyName("cardPlayed")]
        public CardRequest? CardPlayed { get; set; }

        [JsonPropertyName("highInTrick")]
        public CardRequest? HighInTrick { get; set; }
    }

    public sealed class PlayerRequest
    {
        [JsonPropertyName("seat")]
        public int? Seat { get; set; }

        [JsonPropertyName("bid")]
        public int? Bid { get; set; }

        [JsonPropertyName("bidHistory")]
        public List<int>? BidHistory { get; set; }

        [JsonPropertyName("hand")]
        public string? Hand { get; set; }

        [JsonPropertyName("cardsTaken")]
        public string? CardsTaken { get; set; }

        [JsonPropertyName("gameScore")]
        public long GameScore { get; set; }

        [JsonPropertyName("handScore")]
        public int HandScore { get; set; }

        [JsonPropertyName("folded")]
        public bool Folded { get; set; }

        [JsonPropertyName("goodSuit")]
        public int GoodSuit { get; set; }

        [JsonPropertyName("playedCards")]
        public List<PlayedCardRequest>? PlayedCards { get; set; }

        [JsonPropertyName("voidSuits")]
        public List<int>? VoidSuits { get; set; }
    }
}
