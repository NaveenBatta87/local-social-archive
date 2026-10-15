using LocalSocialArchive.Models;

namespace LocalSocialArchive.Services;

public interface IStoryLayoutService
{
    List<StoryCard> GenerateStoryCards(
        IEnumerable<StoryItem> items,
        string authorName,
        string authorHandle,
        string postDateText,
        StoryTheme theme,
        bool isAutoFit = true,
        double customFontSize = 38.0,
        bool includeMedia = true);

    List<StoryCard> GenerateStoryCardsFromPosts(
        IEnumerable<Post> posts,
        StoryTheme theme,
        bool isAutoFit = true,
        double customFontSize = 38.0,
        bool includeMedia = true);
}
