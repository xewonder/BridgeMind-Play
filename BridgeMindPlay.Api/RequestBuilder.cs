using System.Text.RegularExpressions;
using Trickster.Bots;
using Trickster.cloud;

namespace BridgeMindPlay.Api
{
    //  validates an explicit BridgeMind request and maps it onto the real
    //  Trickster.cloud.SuggestCardState<BridgeOptions> consumed by BridgeBot.SuggestNextCard.
    //  Malformed state is rejected with a concise message; nothing is silently coerced to Unknown/0.
    public static class RequestBuilder
    {
        private static readonly Regex rxSeatCards = new(@"^(?:[0-3]\w{2})*$", RegexOptions.Compiled);

        //  Suit values usable in Bridge: Unknown (0, i.e. notrump) plus the four real suits; Joker (5) is not Bridge
        private const int MinBridgeSuit = 1;
        private const int MaxBridgeSuit = 4;
        private const int MinBridgeRank = 2;
        private const int MaxBridgeRank = 14;

        public sealed record Result(string? Error, SuggestCardState<BridgeOptions>? State);

        public static Result Build(SuggestCardRequest request)
        {
            if (request.Players is null)
                return new Result("players is required.", null);

            if (request.Players.Count != 4)
                return new Result("players must contain exactly 4 entries for Bridge.", null);

            var seats = new HashSet<int>();
            foreach (var player in request.Players)
            {
                if (player.Seat is null || player.Bid is null)
                    return new Result("every player requires seat and bid.", null);

                if (player.Seat < 0 || player.Seat > 3 || !seats.Add(player.Seat.Value))
                    return new Result($"player seat {player.Seat} is not a unique Bridge seat (0-3).", null);

                if (!IsValidBid(player.Bid.Value))
                    return new Result($"player bid {player.Bid} is not a known Bridge bid value.", null);

                if (player.BidHistory is not null && player.BidHistory.Any(b => !IsValidBid(b)))
                    return new Result($"player {player.Seat} bidHistory contains an unknown Bridge bid value.", null);

                if (player.GoodSuit < 0 || player.GoodSuit > MaxBridgeSuit)
                    return new Result($"player {player.Seat} goodSuit is not a valid Bridge suit.", null);

                if (player.VoidSuits is not null && player.VoidSuits.Any(s => s < MinBridgeSuit || s > MaxBridgeSuit))
                    return new Result($"player {player.Seat} voidSuits contains an invalid Bridge suit.", null);

                if (player.PlayedCards is not null)
                {
                    foreach (var played in player.PlayedCards)
                    {
                        var error = ValidateCardOrNull(played.CardPlayed, $"player {player.Seat} playedCards.cardPlayed")
                            ?? ValidateCardOrNull(played.HighInTrick, $"player {player.Seat} playedCards.highInTrick");
                        if (error is not null)
                            return new Result(error, null);
                    }
                }
            }

            if (request.Player is null)
                return new Result("player is required.", null);

            if (request.Player.Seat is null || request.Player.Seat < 0 || request.Player.Seat > 3)
                return new Result("player.seat must be a Bridge seat (0-3).", null);

            if (request.Player.Bid is null || !IsValidBid(request.Player.Bid.Value))
                return new Result("player.bid is missing or not a known Bridge bid value.", null);

            if (request.Options is null)
                return new Result("options is required.", null);

            if (request.TrumpSuit is < 0 or > MaxBridgeSuit)
                return new Result($"trumpSuit {request.TrumpSuit} is not a valid Bridge suit (0-4).", null);

            if (request.LegalCards is null)
                return new Result("legalCards is required.", null);

            if (request.LegalCards.Count == 0)
                return new Result("legalCards must contain at least one card.", null);

            if (request.Trick is null)
                return new Result("trick is required (send an empty array when none has been played).", null);

            if (request.CardsPlayed is null)
                return new Result("cardsPlayed is required (send an empty array when none has been played).", null);

            if (request.CardsPlayedInOrder is null)
                return new Result("cardsPlayedInOrder is required (send an empty string for an opening lead).", null);

            if (!rxSeatCards.IsMatch(request.CardsPlayedInOrder))
                return new Result("cardsPlayedInOrder must be zero or more seat(0-3)+card-notation pairs.", null);

            var legal = new List<Card>(request.LegalCards.Count);
            var seen = new HashSet<(int, int)>();
            foreach (var card in request.LegalCards)
            {
                var mapped = MapCard(card, "legalCards", out var error);
                if (error is not null)
                    return new Result(error, null);

                if (!seen.Add((mapped!.suit.GetHashCode(), mapped.rank.GetHashCode())))
                    return new Result($"legalCards contains a duplicate card {mapped}.", null);

                legal.Add(mapped);
            }

            var trick = new List<Card>();
            foreach (var card in request.Trick)
            {
                var mapped = MapCard(card, "trick", out var error);
                if (error is not null)
                    return new Result(error, null);
                trick.Add(mapped!);
            }

            var cardsPlayed = new List<Card>();
            foreach (var card in request.CardsPlayed)
            {
                var mapped = MapCard(card, "cardsPlayed", out var error);
                if (error is not null)
                    return new Result(error, null);
                cardsPlayed.Add(mapped!);
            }

            var cardTakingTrick = MapCard(request.CardTakingTrick, "cardTakingTrick", out var takingError);
            if (takingError is not null)
                return new Result(takingError, null);

            var cloudCard = MapCard(request.CloudCard, "cloudCard", out var cloudError);
            if (cloudError is not null)
                return new Result(cloudError, null);

            if (request.TrickTaker is not null && (request.TrickTaker.Seat is null || !seats.Contains(request.TrickTaker.Seat.Value)))
                return new Result("trickTaker.seat does not match any player.", null);

            //  map the full players collection first so player/trickTaker reference the same instances
            var players = request.Players.Select(MapPlayer).ToList();

            var mappedPlayer = players.FirstOrDefault(p => p.Seat == request.Player!.Seat!.Value);
            if (mappedPlayer is null)
                return new Result("player does not correspond to any entry in players.", null);

            var mappedTrickTaker = request.TrickTaker?.Seat is null
                ? null
                : players.FirstOrDefault(p => p.Seat == request.TrickTaker.Seat!.Value);

            var state = new SuggestCardState<BridgeOptions>
            {
                options = request.Options,
                trumpSuit = (Suit)(request.TrumpSuit ?? 0),
                players = players,
                player = mappedPlayer,
                legalCards = legal,
                trick = trick,
                cardsPlayed = cardsPlayed,
                cardsPlayedInOrder = request.CardsPlayedInOrder,
                cardTakingTrick = cardTakingTrick,
                isPartnerTakingTrick = request.IsPartnerTakingTrick,
                trickTaker = mappedTrickTaker,
                cloudCard = cloudCard,
            };

            return new Result(null, state);
        }

        //  known-good bid integers come from existing Trickster/BridgeBid source; no bidding arithmetic is invented here
        private static bool IsValidBid(int bid)
        {
            return bid is BidBase.NoBid or BidBase.Pass or BidBase.NotPlaying or BidBase.PartnerOfBidWinner or BidBase.Dummy or BidBase.HideBid
                || bid is BridgeBid.Defend or BridgeBid.Double or BridgeBid.Redouble
                || DeclareBid.Is(bid)
                || (bid >= BridgeBid.HCP && bid <= BridgeBid.HCP + 39);
        }

        private static string? ValidateCardOrNull(CardRequest? card, string field)
        {
            if (card is null)
                return null;

            if (card.Suit is null || card.Rank is null)
                return $"{field} must include numeric suit and rank.";

            if (card.Suit < MinBridgeSuit || card.Suit > MaxBridgeSuit)
                return $"{field} contains suit {card.Suit}, which is not a Bridge suit (1-4).";

            if (card.Rank < MinBridgeRank || card.Rank > MaxBridgeRank)
                return $"{field} contains rank {card.Rank}, which is not a Bridge rank (2-14).";

            return null;
        }

        private static Card? MapCard(CardRequest? card, string field, out string? error)
        {
            error = ValidateCardOrNull(card, field);
            if (error is not null || card is null)
                return null;

            return new Card
            {
                suit = (Suit)card.Suit!.Value,
                rank = (Rank)card.Rank!.Value,
                isTrump = card.IsTrump,
            };
        }

        private static PlayerBase MapPlayer(PlayerRequest p)
        {
            return new PlayerBase
            {
                Seat = p.Seat!.Value,
                Bid = p.Bid!.Value,
                BidHistory = p.BidHistory ?? new List<int>(),
                Hand = p.Hand ?? "",
                CardsTaken = p.CardsTaken ?? "",
                GameScore = p.GameScore,
                HandScore = p.HandScore,
                Folded = p.Folded,
                GoodSuit = (Suit)p.GoodSuit,
                PlayedCards = (p.PlayedCards ?? new List<PlayedCardRequest>()).Select(MapPlayedCard).ToList(),
                VoidSuits = p.VoidSuits?.Select(s => (Suit)s).ToList() ?? new List<Suit>(),
            };
        }

        private static PlayedCard MapPlayedCard(PlayedCardRequest p)
        {
            return new PlayedCard
            {
                CardPlayed = MapPlayed(p.CardPlayed),
                HighInTrick = MapPlayed(p.HighInTrick),
            };

            static Card? MapPlayed(CardRequest? c) =>
                c is null || c.Suit is null || c.Rank is null
                    ? null
                    : new Card { suit = (Suit)c.Suit.Value, rank = (Rank)c.Rank.Value, isTrump = c.IsTrump };
        }
    }
}
