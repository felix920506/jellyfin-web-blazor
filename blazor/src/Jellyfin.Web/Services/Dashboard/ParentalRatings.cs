using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>A choice of the "maximum allowed parental rating" select: ratings with the same score share one entry.</summary>
public sealed record ParentalRatingOption(string Name, int? Score, int? SubScore);

/// <summary>The rating list of the parental control tab. Port of the rating logic in features/users/components/ParentalControl.tsx.</summary>
public static class ParentalRatings
{
    /// <summary>Merges consecutive ratings with the same score and sub score into one option named "A/B".</summary>
    public static List<ParentalRatingOption> Group(IEnumerable<ParentalRating> ratings)
    {
        var options = new List<ParentalRatingOption>();
        foreach (var rating in ratings)
        {
            var score = rating.RatingScore?.Score;
            var subScore = rating.RatingScore?.SubScore;
            if (options.Count > 0 && options[^1] is { } last && last.Score == score && last.SubScore == subScore)
            {
                options[^1] = last with { Name = last.Name + "/" + rating.Name };
                continue;
            }

            options.Add(new ParentalRatingOption(rating.Name ?? string.Empty, score, subScore));
        }

        return options;
    }

    /// <summary>
    /// The option that matches a user's limit: the exact score and sub score, otherwise the
    /// highest option not above the score; -1 without a limit or a match.
    /// </summary>
    public static int SelectedIndex(IReadOnlyList<ParentalRatingOption> options, int? maxRating, int? maxSubRating)
    {
        if (maxRating is null)
        {
            return -1;
        }

        var index = -1;
        for (var i = 0; i < options.Count; i++)
        {
            if (options[i].Score == maxRating && options[i].SubScore == maxSubRating)
            {
                index = i;
            }
        }

        if (index >= 0)
        {
            return index;
        }

        for (var i = 0; i < options.Count; i++)
        {
            if (options[i].Score is { } score && score <= maxRating)
            {
                index = i;
            }
        }

        return index;
    }
}
