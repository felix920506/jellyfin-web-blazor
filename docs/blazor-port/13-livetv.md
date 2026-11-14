# Live TV in the Blazor port

The Live TV workstream ports the legacy `/livetv` page, its guide and recording controls, and the
Live TV extension points on `/details`. The original client is `src/controllers/livetv/` and the
recording components under `src/components/recordingcreator/`.

## 1. Page and tabs

Route `/livetv` is implemented by `Pages/LiveTv/LiveTvPage.razor`. It keeps the original six-tab
order and labels:

1. Programs
2. Guide
3. Channels
4. Recordings
5. Schedule
6. Series

`Services/LiveTv/LiveTvData.cs` owns the Live TV API calls and maps the SDK models into the page's
view models. `LiveTvGrouping` groups programmes by local calendar day and category, while the
guide components preserve the legacy channel rows, date tabs, programme cells, scrolling and
settings dialog classes. `Styles/port/_livetv.scss` contains the page-level layout rules that are
not supplied by the guide component stylesheet.

The page responds to the same refresh sources as the legacy client: server Live TV events,
library changes, language changes and layout changes. Selecting a programme in the guide follows
the modern port's route to `/details`, where the recording fields are available for the programme.

## 2. Recording controls

The recording implementation is split into reusable components and a service:

| Original | Port |
| --- | --- |
| `recordinghelper.js` | `Services/LiveTv/RecordingHelper.cs` |
| `recordingfields.js` | `Components/LiveTv/RecordingFields.razor` |
| `recordingcreator.js` | `Components/LiveTv/RecordingCreator.razor` |
| `recordingeditor.js` | `Components/LiveTv/RecordingEditor.razor` |
| `seriesrecordingeditor.js` | `Components/LiveTv/SeriesRecordingEditor.razor` |
| Live TV item shortcuts and menu commands | `Services/LiveTv/LiveTvCommands.cs` |

The controls support:

- scheduling and cancelling one programme;
- converting a programme recording to a series recording;
- cancelling a series recording;
- single-recording padding options;
- series options for new/all episodes, channel, air time, keep count, skip-in-library and
  padding;
- confirmation dialogs and the original recording toast messages;
- editing timers from cards, list rows, the item menu and the details page.
- the video OSD's current-programme record toggle for users with Live TV management permission;
  its timer state is loaded from the programme endpoint and refreshed after create/cancel.

`LiveTvCommandHandler` deliberately owns `record` and `programdialog` only for `Program` items.
Timer and series-timer items retain their own edit and cancellation commands, while
`ToggleRecording` remains available for programmes and items that carry a timer identifier.

## 3. Details-page extension points

`Services/Details/DetailsLiveTv.cs` provides the details page with:

- `RecordingFields(program)` for the single and series record buttons;
- `SeriesTimerEditor(timer)` for embedded series-timer settings;
- `SeriesTimerSchedule(timer)` for upcoming episodes;
- `ChannelGuide(channel)` for a channel's grouped programme guide;
- timer and series-timer cancellation helpers.

`Pages/Details/ItemDetails.razor` decides whether these fragments apply using `DetailsRules`, so
the page does not need Live TV-specific branching in its shared markup beyond the extension
points. The details page wraps `RecordingFields` in `.recordingFields`; the component has a
`Bare` mode to avoid producing a second wrapper.

## 4. Test data and automated coverage

`scripts/seed-livetv.sh` seeds a private Jellyfin test-server instance with:

- six M3U channels and XMLTV guide data;
- multiple categories and programme flags (live, new, premiere and repeat);
- guide data spanning several local days;
- `EnableLiveTvManagement` enabled for the test user.

The script uses `/media/livetv.m3u` and `/media/livetv.xml`, so it does not change the repository's
normal test-server fixture. The private-server E2E entry point is:

```sh
JELLYFIN_URL=http://localhost:18098 ./dev e2e livetv.py
```

`e2e/livetv.py` checks all six tabs, guide date switching, guide settings, desktop and phone
layouts, programme details, single-recording settings, series-recording settings, and cancellation
of both timer types. Its XMLTV fixture schedules programmes relative to the current UTC hour so
the visible guide does not depend on when the test runs. Screenshots are written to
`blazor/artifacts/e2e/`.

The bUnit suite is in `tests/Jellyfin.Web.Tests/LiveTv/`:

- `LiveTvPageTests` covers tabs, guide rendering, settings and command ownership;
- `RecordingDialogTests` covers programme controls, recording and series editors, cancellation,
  embedded series settings and the bare details-page controls.

At completion, the focused Live TV suite contains **68 passing tests** and the Playwright scenario
finishes with `livetv: ok`.

## 5. Gaps and intentional differences

- Guide programme clicks route to the port's details page rather than opening the legacy programme
  dialog directly; the same recording fields and API operations are available there.
- The channel guide and schedule use Blazor list containers and local-calendar grouping while
  preserving the original classes, labels and request parameters.
- Guide refresh is provided by the server's scheduled refresh after the seed script registers the
  tuner and listing provider; the seed script tolerates servers that do not expose the legacy
  manual refresh endpoint.
- Shared port gaps remain outside this workstream: the shell's MudBlazor typography/reset needs a
  general fix, and list/scroller/context-menu implementation differences are documented by their
  respective workstreams.
