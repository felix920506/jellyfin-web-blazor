using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;

namespace Jellyfin.Web.Tests.Items;

/// <summary>The server writes ids without dashes; models parsed outside the SDK client must still get them.</summary>
public class KiotaJsonGuidTests
{
    private const string MovieId = "38b5a310634fa3822698eb433507f671";

    [Fact]
    public void Ids_without_dashes_are_read()
    {
        var item = KiotaJson.Parse(
            $$"""
            {
              "Id": "{{MovieId}}", "Name": "Movie", "Type": "Movie",
              "ParentId": "f137a2dd-21bb-c1b9-9aa5-c0f6bf02a805",
              "ImageTags": { "Primary": "0123456789abcdef0123456789abcdef" },
              "UserData": { "ItemId": "{{MovieId}}", "Played": true }
            }
            """,
            BaseItemDto.CreateFromDiscriminatorValue)!;

        Assert.Equal(Guid.Parse(MovieId), item.Id);
        Assert.Equal(Guid.Parse("f137a2dd21bbc1b99aa5c0f6bf02a805"), item.ParentId);
        Assert.Equal(Guid.Parse(MovieId), item.UserData!.ItemId);
        Assert.Equal(MovieId, item.IdText());
        Assert.Equal("0123456789abcdef0123456789abcdef", item.ImageTag("Primary"));
        Assert.Equal("Movie", item.TypeName());
    }

    [Fact]
    public void Item_lists_are_read_from_a_query_result_and_from_a_plain_array()
    {
        var fromResult = ItemQueries.ParseItems($$"""{"Items":[{"Id":"{{MovieId}}","Name":"A"}],"TotalRecordCount":1}""");
        var fromArray = ItemQueries.ParseItems($$"""[{"Id":"{{MovieId}}","Name":"A"},{"Name":"B"}]""");

        Assert.Equal(Guid.Parse(MovieId), Assert.Single(fromResult).Id);
        Assert.Equal(2, fromArray.Count);
        Assert.Equal(Guid.Parse(MovieId), fromArray[0].Id);
        Assert.Null(fromArray[1].Id);
    }

    [Fact]
    public void A_clone_keeps_the_ids()
    {
        var item = new BaseItemDto { Id = Guid.Parse(MovieId), Name = "Movie", UserData = new UserItemDataDto { ItemId = Guid.Parse(MovieId) } };

        var copy = KiotaJson.Clone(item, BaseItemDto.CreateFromDiscriminatorValue);

        Assert.NotSame(item, copy);
        Assert.Equal(item.Id, copy.Id);
        Assert.Equal(item.Id, copy.UserData!.ItemId);
    }
}
