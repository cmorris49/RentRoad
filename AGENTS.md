
# RentRoad — Codex Instructions

## Project Conventions

Before making any changes, read and follow all instructions in:

`.github/copilot-instructions.md`

These instructions define the project's coding conventions, C# documentation requirements, MVC architecture, security standards, database practices, and UI conventions.

## Development Rules

- Follow SOLID, DRY, KISS, and proper object-oriented design.
- Maintain separation of concerns between Models, Controllers, Services, and Views.
- Add XML documentation comments above every C# method, including private methods.
- Do not add inline comments inside methods.
- Follow the naming conventions in the Copilot instructions.
- Keep changes focused on the requested task.
- Inspect existing files before creating or modifying components.
- Reuse existing code where appropriate.
- Avoid unnecessary abstractions and overengineering.

## Database Rules

- Use Entity Framework Core with Azure SQL Database (Microsoft SQL Server).
- Preserve ASP.NET Core Identity and existing data.
- Design application tables according to 1NF, 2NF, and 3NF.
- Use explicit foreign keys, constraints, and appropriate delete behaviours.
- Preserve historical rental and messaging records.
- Store listing images in Azure Blob Storage and only their metadata in Azure SQL Database.
- Read the database connection string from `ConnectionStrings:DefaultConnection` using user secrets or environment variables; never commit credentials.
- Existing SQLite migrations are provider-specific history. Do not apply them to SQL Server or replace them without approval.
- Do not generate or apply migrations without explicit approval.
- Never delete or recreate the database without permission.

## AI Features

The only generative AI feature is AI-generated review summaries.

Keep the course-required simple predictive user model separate from generative AI.

Do not implement AI listing descriptions, AI recommendations, or AI-generated bundles.

## Working Process

1. Inspect the relevant files and existing architecture.
2. Identify any problems or conflicts before modifying code.
3. Make focused, maintainable changes.
4. Run `dotnet build` after modifying C# code.
5. Report the files changed and explain important decisions.
6. Report any errors, warnings, or unfinished work.

Do not perform unrelated refactoring or modify existing migrations without permission.
