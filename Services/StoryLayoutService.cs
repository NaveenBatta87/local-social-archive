using System.Collections.ObjectModel;
using System.IO;
using LocalSocialArchive.Models;

namespace LocalSocialArchive.Services;

public class StoryLayoutService : IStoryLayoutService
{
    private const double CanvasContentWidth = 920.0;
    private const double MaxAvailableCardHeight = 1320.0; // 1920 - 130(top) - 160(bottom) - 210(header) - 100(footer)
    private const double ImageEstimatedHeight = 480.0;

    public List<StoryCard> GenerateStoryCardsFromPosts(
        IEnumerable<Post> posts,
        StoryTheme theme,
        bool isAutoFit = true,
        double customFontSize = 38.0,
        bool includeMedia = true)
    {
        var postList = posts.OrderBy(p => p.ThreadIndex).ToList();
        if (postList.Count == 0)
        {
            return GenerateEmptyFallback(theme);
        }

        var first = postList[0];
        string authorName = !string.IsNullOrWhiteSpace(first.AuthorName) ? first.AuthorName : "Author";
        string authorHandle = !string.IsNullOrWhiteSpace(first.AuthorHandle) ? first.AuthorHandle : "@author";
        string postDateText = first.PostDate.ToString("MMMM dd, yyyy • h:mm tt");

        var items = new List<StoryItem>();
        int idx = 1;
        foreach (var p in postList)
        {
            string? mediaPath = null;
            if (p.MediaItems.Count > 0)
            {
                mediaPath = p.MediaItems.FirstOrDefault(m => !string.IsNullOrEmpty(m.FilePath) && File.Exists(m.FilePath))?.FilePath;
            }

            items.Add(new StoryItem
            {
                SegmentIndex = idx++,
                Content = p.Content,
                MediaPath = mediaPath,
                IncludeMedia = includeMedia
            });
        }

        return GenerateStoryCards(items, authorName, authorHandle, postDateText, theme, isAutoFit, customFontSize, includeMedia);
    }

    public List<StoryCard> GenerateStoryCards(
        IEnumerable<StoryItem> rawItems,
        string authorName,
        string authorHandle,
        string postDateText,
        StoryTheme theme,
        bool isAutoFit = true,
        double customFontSize = 38.0,
        bool includeMedia = true)
    {
        var itemList = rawItems.ToList();
        if (itemList.Count == 0)
        {
            return GenerateEmptyFallback(theme);
        }

        double fontSize = isAutoFit ? CalculateAutoFontSize(itemList, includeMedia) : Math.Clamp(customFontSize, 28.0, 52.0);

        // Normalize author display
        string cleanAuthor = string.IsNullOrWhiteSpace(authorName) ? "Author" : authorName.Trim();
        string cleanHandle = string.IsNullOrWhiteSpace(authorHandle) ? "@author" : (authorHandle.StartsWith('@') ? authorHandle.Trim() : "@" + authorHandle.Trim());
        string initial = !string.IsNullOrWhiteSpace(cleanAuthor) ? cleanAuthor.Substring(0, 1).ToUpperInvariant() : "A";

        // Prepare items (if a single item is excessively long, break it into smaller paragraph segments)
        var normalizedItems = new List<StoryItem>();
        int segmentCounter = 1;

        foreach (var item in itemList)
        {
            item.IncludeMedia = includeMedia;
            double estimatedItemHeight = EstimateItemHeight(item, fontSize, includeMedia);

            if (estimatedItemHeight > MaxAvailableCardHeight)
            {
                // Break giant post into chunks at paragraph or sentence breaks
                var chunks = SplitLongContent(item.Content, fontSize);
                for (int c = 0; c < chunks.Count; c++)
                {
                    normalizedItems.Add(new StoryItem
                    {
                        SegmentIndex = segmentCounter++,
                        Content = chunks[c],
                        MediaPath = (c == 0) ? item.MediaPath : null,
                        IncludeMedia = includeMedia
                    });
                }
            }
            else
            {
                item.SegmentIndex = segmentCounter++;
                normalizedItems.Add(item);
            }
        }

        // Pack items into cards
        var cards = new List<StoryCard>();
        var currentCardItems = new List<StoryItem>();
        double currentCardHeight = 0.0;

        foreach (var item in normalizedItems)
        {
            double itemHeight = EstimateItemHeight(item, fontSize, includeMedia);
            double itemGap = currentCardItems.Count > 0 ? 32.0 : 0.0;

            if (currentCardItems.Count > 0 && (currentCardHeight + itemGap + itemHeight) > MaxAvailableCardHeight)
            {
                // Current card is full -> finalize card and start new one
                cards.Add(CreateCard(cards.Count + 1, currentCardItems, cleanAuthor, cleanHandle, initial, postDateText, theme, fontSize));
                currentCardItems = new List<StoryItem> { item };
                currentCardHeight = itemHeight;
            }
            else
            {
                currentCardItems.Add(item);
                currentCardHeight += (itemGap + itemHeight);
            }
        }

        if (currentCardItems.Count > 0)
        {
            cards.Add(CreateCard(cards.Count + 1, currentCardItems, cleanAuthor, cleanHandle, initial, postDateText, theme, fontSize));
        }

        // Update TotalCards count on all cards
        int total = cards.Count;
        foreach (var card in cards)
        {
            card.TotalCards = total;
        }

        return cards;
    }

    private static StoryCard CreateCard(
        int index,
        List<StoryItem> items,
        string author,
        string handle,
        string initial,
        string dateText,
        StoryTheme theme,
        double fontSize)
    {
        var card = new StoryCard
        {
            CardIndex = index,
            TotalCards = index,
            AuthorName = author,
            AuthorHandle = handle,
            AvatarInitial = initial,
            PostDateText = dateText,
            Theme = theme,
            FontSize = fontSize,
            Items = new ObservableCollection<StoryItem>(items)
        };
        return card;
    }

    private static double EstimateItemHeight(StoryItem item, double fontSize, bool includeMedia)
    {
        // Container base padding: 36px top, 36px bottom, plus index header ~40px = 112px
        double height = 112.0;

        // Estimate text line count
        int charsPerLine = Math.Max(25, (int)(CanvasContentWidth / (fontSize * 0.52)));
        string[] paragraphs = item.Content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        int totalLines = 0;

        foreach (var p in paragraphs)
        {
            if (string.IsNullOrWhiteSpace(p))
            {
                totalLines += 1;
            }
            else
            {
                int pLines = (int)Math.Ceiling((double)p.Length / charsPerLine);
                totalLines += Math.Max(1, pLines);
            }
        }

        double lineHeight = fontSize * 1.45;
        height += (totalLines * lineHeight);

        // Include media height if present
        if (includeMedia && item.HasMedia)
        {
            height += ImageEstimatedHeight;
        }

        return height;
    }

    private static double CalculateAutoFontSize(List<StoryItem> items, bool includeMedia)
    {
        if (items.Count == 1)
        {
            var single = items[0];
            bool hasImage = includeMedia && single.HasMedia;
            if (single.Content.Length < 180 && !hasImage) return 44.0;
            if (single.Content.Length < 320 && !hasImage) return 40.0;
            return 38.0;
        }

        if (items.Count == 2)
        {
            int totalLen = items.Sum(i => i.Content.Length);
            bool hasAnyImage = includeMedia && items.Any(i => i.HasMedia);
            if (totalLen < 300 && !hasAnyImage) return 40.0;
            return 38.0;
        }

        return 38.0;
    }

    private static List<string> SplitLongContent(string text, double fontSize)
    {
        var result = new List<string>();
        var paragraphs = text.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

        var currentChunk = new System.Text.StringBuilder();
        foreach (var p in paragraphs)
        {
            if (currentChunk.Length + p.Length > 900 && currentChunk.Length > 0)
            {
                result.Add(currentChunk.ToString().Trim());
                currentChunk.Clear();
            }
            currentChunk.AppendLine(p);
            currentChunk.AppendLine();
        }

        if (currentChunk.Length > 0)
        {
            result.Add(currentChunk.ToString().Trim());
        }

        if (result.Count == 0 && !string.IsNullOrWhiteSpace(text))
        {
            result.Add(text.Trim());
        }

        return result;
    }

    private static List<StoryCard> GenerateEmptyFallback(StoryTheme theme)
    {
        var card = new StoryCard
        {
            CardIndex = 1,
            TotalCards = 1,
            AuthorName = "Thread Author",
            AuthorHandle = "@author",
            AvatarInitial = "T",
            PostDateText = DateTime.Now.ToString("MMMM dd, yyyy"),
            Theme = theme,
            FontSize = 38.0,
            Items = new ObservableCollection<StoryItem>
            {
                new StoryItem
                {
                    SegmentIndex = 1,
                    Content = "No thread content found. Paste a thread or import from Thread Reader to generate story cards."
                }
            }
        };
        return new List<StoryCard> { card };
    }
}
