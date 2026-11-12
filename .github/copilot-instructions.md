# RentRoad — GitHub Copilot Instructions

## 1. Project Overview

RentRoad is a peer-to-peer equipment rental marketplace built using ASP.NET Core MVC and .NET 10.

Users can browse and search rental listings, create and manage their own listings, request rentals, approve or decline requests, communicate with other users, and leave reviews.

A single user can act as both a renter and an owner.

### Technology Stack

- Backend: C# / ASP.NET Core MVC (.NET 10)
- Frontend: Razor Views, HTML, CSS, JavaScript, Bootstrap 5
- Database: Azure SQL Database (Microsoft SQL Server)
- ORM: Entity Framework Core
- Authentication: ASP.NET Core Identity
- IDE: Visual Studio Code
- AI functionality: AI-generated review summaries only

Follow the existing project structure and conventions whenever appropriate.

## 2. General Coding Principles

- Follow SOLID principles, particularly Single Responsibility and Dependency Inversion.
- Keep code simple, readable, modular, and maintainable.
- Follow DRY (Don't Repeat Yourself) and KISS (Keep It Simple, Stupid).
- Avoid unnecessary abstractions, complexity, and overengineering.
- Each class should have one clear responsibility.
- Each method should perform one well-defined operation.
- Prefer small, focused methods rather than large methods handling multiple responsibilities.
- Avoid duplicated business logic.
- Use meaningful, descriptive names for classes, methods, variables, and properties.
- Avoid magic numbers and hardcoded configuration values. Use named constants or configuration where appropriate.
- Do not implement unrelated features or make unnecessary changes to existing code.
- Prefer built-in .NET functionality over unnecessary third-party packages.

## 3. Comments and Documentation

**Every method must have a documentation comment immediately above its declaration.**

For C#:

- Use XML documentation comments (`///`) with `<summary>` for every method, including private methods.
- Include `<param>` and `<returns>` when they provide useful information.
- Describe what the method does, not how individual lines work.
- Keep comments concise, accurate, and professional.
- Document constructors and important classes where appropriate.
- Do not add inline comments within method bodies.
- Do not add comments after individual statements.
- Do not add unnecessary comments explaining obvious code.
- When modifying a method, update its documentation if its behaviour changes.

Example:

```csharp
/// <summary>
/// Retrieves all rental listings belonging to the specified owner.
/// </summary>
/// <param name="ownerId">The unique identifier of the owner.</param>
/// <returns>The owner's rental listings.</returns>
public async Task<List<Listing>> GetOwnerListingsAsync(string ownerId)
{
    return await _context.Listings
        .Where(listing => listing.OwnerId == ownerId)
        .ToListAsync();
}
```

For JavaScript, use JSDoc comments above functions and methods. Avoid inline comments inside function bodies.

## 4. C# Naming Conventions

Follow standard Microsoft C# naming conventions.

- Classes, interfaces, methods, and public properties: `PascalCase`
- Local variables and parameters: `camelCase`
- Private fields: `camelCase`
- Interfaces: prefix with `I`, such as `IListingService`
- Asynchronous methods: suffix with `Async`
- Constants: UPPERCASE
- Use descriptive names instead of abbreviations.

Enable nullable reference types and handle null values appropriately.

Use `async` and `await` for asynchronous I/O operations. Avoid blocking asynchronous operations with `.Result` or `.Wait()`.

## 5. ASP.NET Core MVC Architecture

Maintain separation of concerns between Models, Views, Controllers, and Services.

### Models

- Represent application entities and their relationships.
- Use appropriate data types and validation constraints.
- Keep models focused on their data and domain responsibilities.
- Do not place HTTP request handling or presentation logic in entity models.

### Controllers

- Handle HTTP requests and return appropriate responses.
- Keep controllers thin.
- Delegate business logic and database operations to services.
- Use dependency injection instead of constructing services directly.
- Validate incoming requests and handle invalid model states.
- Use the appropriate HTTP methods for different operations.

### Services

- Contain reusable business logic.
- Use interfaces for important business services when they provide meaningful separation or testability.
- Keep services independent of Razor Views and presentation details.
- Use asynchronous database operations.
- Return meaningful results or errors to callers.

### Views and ViewModels

- Use strongly typed Razor Views.
- Use ViewModels for forms and pages that require specialized data.
- Avoid exposing sensitive entity fields through form binding.
- Keep Razor Views focused on presentation.
- Do not perform database queries or complex business logic in Views.
- Use partial views or reusable components for repeated UI elements.

## 6. Entity Framework Core and Database

- Use EF Core with the SQL Server provider for Azure SQL Database access.
- Read the connection string from `ConnectionStrings:DefaultConnection`; configure it through user secrets or environment variables without committing credentials.
- Store listing images in Azure Blob Storage and only their metadata in Azure SQL Database.
- Use the existing ApplicationDbContext where appropriate.
- Define relationships, foreign keys, and constraints explicitly when necessary.
- Use LINQ rather than raw SQL unless there is a justified reason otherwise.
- Use asynchronous EF Core methods for database operations.
- Prevent unnecessary database queries and N+1 query problems.
- Use `AsNoTracking()` for read-only queries when appropriate.
- Use transactions when operations require multiple coordinated database changes.
- Use EF Core migrations to manage schema changes.
- Do not silently delete, recreate, or reset the database.
- Do not modify existing migrations without explaining why.
- Preserve existing data whenever possible.
- Keep provider-specific migration histories separate. Do not apply the existing SQLite migrations to SQL Server or replace them without approval.

Persist monetary amounts as integer CAD cents (`long`). Use `decimal` for currency conversions or calculations when needed. Use `DateOnly` and `TimeOnly` for rental periods and UTC `DateTime` values for timestamps.

## 7. Authentication and Security

Use ASP.NET Core Identity for authentication and account management.

- Do not create a custom password authentication system.
- Use `[Authorize]` to protect authenticated functionality.
- Verify resource ownership before modifying listings or rental requests.
- Never trust a user ID submitted through a form as proof of ownership.
- Use server-side validation even when client-side validation exists.
- Protect state-changing requests against CSRF using ASP.NET Core anti-forgery mechanisms.
- Never place passwords, API keys, or connection secrets directly in source code.
- Use user secrets or environment variables for sensitive configuration.
- Do not expose sensitive information in responses, logs, or error messages.
- Validate uploaded files, including file type and size.
- Use safe, parameterized database access through EF Core.
- Handle exceptions without exposing internal implementation details.

## 8. RentRoad Business Rules

Keep the following application rules consistent throughout the project:

- A registered user may act as both a renter and an owner.
- Users may only edit or delete listings they own.
- Users cannot rent their own listings.
- Rental dates must be valid and cannot conflict with unavailable dates.
- Multiple pending requests may exist for the same dates, but overlapping approved or active rentals must be prevented.
- Rental prices must be calculated consistently and displayed in CAD.
- Owners may only approve or decline requests for their own listings.
- Rental status changes must follow valid state transitions.
- Users may only review rentals they completed.
- Each completed rental may receive at most one review from its renter.
- Users may only access messages and conversations they are authorized to view.

Place these rules in the appropriate service layer rather than duplicating them across controllers.

## 9. Frontend and UI Conventions

- Follow the RentRoad Figma prototype and its revised layouts when provided.
- Use Bootstrap 5 and consistent styling across pages.
- Maintain a responsive design for desktop and mobile.
- Use semantic HTML and accessible form controls.
- Provide readable labels, sufficient contrast, and keyboard accessibility.
- Display clear validation errors, confirmation messages, and loading states where appropriate.
- Use consistent terminology and navigation.
- Separate My Rentals (renter activity) and My Listings (owner activity).
- Make Create Listing and Incoming Requests easy to discover.
- Use Vancouver, British Columbia as the initial location context, CAD for prices, and kilometres for distances.
- Keep custom CSS organized and avoid unnecessary inline styles.
- Keep JavaScript modular and use classes when they provide meaningful organization.

Do not invent Figma designs or assume visual details that have not been provided.

## 10. AI and Predictive Features

The only planned generative AI feature is summarizing existing rental item reviews.

- Keep AI integration in a dedicated service.
- Clearly identify AI-generated content in the interface.
- Handle missing reviews, API errors, and invalid AI responses.
- Keep API keys outside source code.
- Do not implement AI listing descriptions, AI-powered recommendations, or AI-generated bundles.

The course also requires a simple predictive user model. If implemented, keep it separate from the generative AI service and do not introduce additional AI integrations without approval.

## 11. Error Handling and Validation

- Validate inputs at the appropriate application boundaries.
- Use Data Annotations and ModelState validation for forms.
- Use service-level checks for business rules.
- Return clear, actionable messages when operations fail.
- Handle invalid IDs, missing records, unauthorized actions, and database errors.
- Use centralized exception handling where appropriate.
- Log unexpected errors without exposing sensitive information.
- Do not silently catch and ignore exceptions.
- Preserve user input when form validation fails.

## 12. Testing and Code Quality

- Write code that is easy to test.
- Use dependency injection to isolate components where practical.
- Test business rules separately from controller and UI behaviour.
- Include tests for valid inputs, invalid inputs, authorization, and edge cases.
- Pay particular attention to rental date conflicts, ownership checks, and status transitions.
- Ensure new functionality builds successfully.
- Avoid introducing compiler warnings where possible.
- Do not remove existing functionality unless explicitly requested.

## 13. Git and Project Organization

- Keep files in appropriate folders based on responsibility.
- Do not create unnecessary project layers or folders.
- Keep related code organized and easy to locate.
- Never commit secrets, local database files, build outputs, or temporary files.
- Respect the existing `.gitignore`.
- Keep changes focused on the requested feature.
- Do not make unrelated refactors.

## 14. Instructions for Copilot When Implementing Features

Before writing code:

1. Inspect the existing project structure and relevant files.
2. Follow the conventions defined in this document.
3. Reuse existing components and patterns where appropriate.
4. Identify the models, services, controllers, and views needed.
5. Avoid implementing functionality beyond the requested scope.

When writing code:

1. Follow proper MVC separation of concerns.
2. Add XML documentation above every C# method.
3. Do not use inline comments inside methods.
4. Use meaningful names and clear object-oriented design.
5. Include appropriate validation, authorization, and error handling.
6. Avoid duplicated or unnecessarily complex code.

After making changes:

1. Summarize the files created or modified.
2. Explain important architectural decisions.
3. Identify any database migrations or configuration changes required.
4. Provide commands to build and test the changes.
5. Explain any remaining limitations or unfinished functionality.

Prioritize correctness, clarity, and maintainability over implementing features as quickly as possible.
