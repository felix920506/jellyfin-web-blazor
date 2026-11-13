using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services.Playback;

/// <summary>
/// Item commands whose target is the current playback queue. These are the command forms used by
/// the original item context menu and queue list (<c>clearQueue</c>, <c>playmenu</c> and
/// <c>setplaylistindex</c>).
/// </summary>
[AppService(typeof(IItemCommandHandler))]
public sealed class PlaybackItemCommandHandler(
    PlaybackManager playback,
    AppDialogs dialogs,
    Globalize globalize) : IItemCommandHandler
{
    public bool CanHandle(string command, BaseItemDto item) => command is
        ItemCommand.ClearQueue or ItemCommand.PlayMenu or ItemCommand.SetPlaylistIndex;

    public async Task<ItemCommandResult> ExecuteAsync(string command, BaseItemDto item, ItemActionContext context)
    {
        switch (command)
        {
            case ItemCommand.ClearQueue:
                playback.ClearQueue();
                break;
            case ItemCommand.SetPlaylistIndex:
                if (!string.IsNullOrEmpty(item.PlaylistItemId))
                {
                    await playback.SetCurrentPlaylistItemAsync(item.PlaylistItemId);
                }

                break;
            case ItemCommand.PlayMenu:
                await PlayMenuAsync(item);
                break;
        }

        return ItemCommandResult.None;
    }

    private async Task PlayMenuAsync(BaseItemDto item)
    {
        var id = item.TypeName() == "Program" ? item.ChannelId.IdText() : item.ActionId();
        var position = item.UserData?.PlaybackPositionTicks ?? 0;
        if (position <= 0 || item.IsFolder == true)
        {
            await playback.PlayAsync(new PlayOptions { Ids = [id] });
            return;
        }

        var choice = await dialogs.ActionSheetAsync(new ActionSheetOptions
        {
            Items =
            [
                new ActionSheetItem
                {
                    Id = "resume",
                    Name = globalize.Translate("ResumeAt", DateTimeText.GetDisplayRunningTime(position)),
                },
                new ActionSheetItem { Id = "play", Name = globalize["PlayFromBeginning"] },
            ],
        });

        if (choice == "resume")
        {
            await playback.PlayAsync(new PlayOptions { Ids = [id], StartPositionTicks = position });
        }
        else if (choice == "play")
        {
            await playback.PlayAsync(new PlayOptions { Ids = [id] });
        }
    }
}
