# Project Honorific

Project Honorific reads a local project status bridge file and sends the current work state to Caraxi's Honorific plugin.

Default bridge path:

```text
%USERPROFILE%\.codex\project-honorific-status.json
```

Example status:

```json
{
  "version": 1,
  "updatedAt": "2026-06-21T12:00:00.0000000+00:00",
  "project": "Better Deaths",
  "activity": "Developing",
  "title": "",
  "details": "Working on Last Pull Review",
  "category": "development",
  "toolProvider": "codex",
  "detectable": true
}
```

If `title` is blank, the plugin builds a title from `activity` and `project`, such as `Developing Better Deaths`.

Title format:

```text
Developing <Project Name>
Auditing <Project Name>
Building <Project Name>
Testing <Project Name>
Committing <Project Name>
Updating Changelog for <Project Name>
Pushing update to <Project Name>
```

Activity aliases are normalized before display, so short forms such as `dev`, `audit`, `build`, `test`, `commit`, `changelog`, `release`, and `push` still produce the matching title format.

Honorific payload support:

- Title text
- Prefix or suffix placement
- Title color
- Solid glow color
- Preset gradients
- Two-color gradients
- Gradient animation style: Wave, Pulse, or Static

Preset and two-color gradients are Honorific supporter/donator visual options. Project Honorific can send those IPC fields, while Honorific controls how they render in-game.

Commands:

```text
/projecthonorific
/phonorific
/projecthonorific reload
/projecthonorific clear
/projecthonorific sample
```

Local helper:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\set-project-status.ps1 -Project "Better Deaths" -Activity "Developing" -Details "Working on Last Pull Review"
```

The helper can infer the project from the current git repository:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\set-project-status.ps1 -Activity Developing
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\set-project-status.ps1 -Activity Changelog
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\set-project-status.ps1 -Activity Pushing
```

Known repository names are normalized into friendly project names such as `Better Deaths`, `DMU Helper`, `Chibi Chaos`, and `Project Honorific`.

Handwritten title rotation:

- The generated project title is always part of the rotation.
- Add handwritten titles in the plugin window to rotate between the project title and custom title text.
- The generated project title has its own duration.
- Each handwritten title has its own duration slider in the plugin window.
- When the status is stale, the idle title replaces the generated project title and still rotates with handwritten titles.

Privacy guard:

- Project inference only runs when the workspace is inside the trusted project root.
- The trusted project root defaults to `%USERPROFILE%\Projects`, the folder containing these local project folders.
- The bridge file stores only the workspace folder name, not the full local path.
- If the workspace is outside that root, pass `-Project` explicitly or set `-ProjectRoot` to the folder that contains the project folders.
