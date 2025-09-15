## Important Dev Guidelines

- Refer to the document /Users/kevinmccreary/appostolic/SnapshotArchitecture.md for detail of current system state. This document should be kept up to date with the completion of stories. Particularly important is the folder / file structure tree.
- When creating a dotnet migration, be sure to create the associated .Designer.cs file. Also remember to run the db update for the migration. Use 'make migrate'.
- At the conclusion of every story, append storyLog with the exact summary that you provide in the Copilot chat window.
- At the conclusion of every story, commit with msg and sync.
- if a sprint guide is being used and it lists stories, mark each story complete with ✅ DONE when it is complete.
- add code comments above each new method, controller, function, etc. that reflect the purpose of the code. This will be important for dev documentation. If a code block is complex, add code comments explaining the purpose of the code block.
