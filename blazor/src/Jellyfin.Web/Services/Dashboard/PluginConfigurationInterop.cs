using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Jellyfin.Web.Services;

/// <summary>Owns an isolated legacy plugin document, never the Blazor render tree.</summary>
public interface IPluginConfigurationInterop
{
    ValueTask<IJSObjectReference> MountAsync(ElementReference host, string html, string name);
}

[AppService(typeof(IPluginConfigurationInterop))]
public sealed class PluginConfigurationInterop(IJSRuntime js, JellyfinApi api, Globalize globalize) : IPluginConfigurationInterop
{
    private Task<IJSObjectReference>? _module;

    public async ValueTask<IJSObjectReference> MountAsync(ElementReference host, string html, string name)
    {
        var module = await (_module ??= js.InvokeAsync<IJSObjectReference>("import", "./js/plugincompat.js").AsTask());
        return await module.InvokeAsync<IJSObjectReference>("mount", host, html, new
        {
            name,
            serverAddress = api.ServerAddress,
            accessToken = api.AccessToken,
            userId = api.UserIdText,
            authorization = api.AuthorizationHeader(),
            culture = globalize.Culture,
            dictionaryCulture = Globalize.ResolveDictionaryCulture(globalize.Culture),
            savedText = globalize["SettingsSaved"],
            errorText = globalize["PluginLoadConfigError"],
        });
    }
}
