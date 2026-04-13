## Product Intent

For-Rest is a local-first API workbench for developers, testers, architects, and security-minded power users. It is being built as a fast, text-forward, pane-driven desktop and mobile application that makes working with HTTP requests feel closer to using an IDE than a typical form-heavy REST client. The long-term vision is a polished, high-capability tool for composing requests, managing workspaces and environments, inspecting responses, running tests, scripting dynamic request flows, and building repeatable API workflows without depending on a cloud backend.

The product is intended to grow into a serious developer platform, not just a basic request sender. Over time, For-Rest should support rich request authoring, variable management, response extraction, scheduling and repeat execution, advanced scripting, test automation, and third-party extensibility through a plugin system. Its UI should remain clean, compact, and highly usable, with a true three-pane layout, tabbed work surfaces, strong textual density, fluid pane movement, and editor-style surfaces for payloads, scripts, and inspection. The goal is to create a modern, powerful, commercial-ready API client that feels smooth, intentional, and extensible from the start.

## Current Baseline

Date: 2026-03-17

- Solution format: `ForRest.slnx`
- SDK baseline: `.NET 10.0.200-preview.0.26103.119` pinned in [`global.json`](global.json)
- App model: Maui Windows and Android apps
- Primary storage: Maui Local Storage

## Repo Map

```text
src/
  ForRest.App/                    Maui app shell and presentation view models
  ForRest.Domain/                 Variable resolution, request compilation, JSON helpers, extraction
  ForRest.Models/                 DTOs, enums, persisted models, execution records
  ForRest.Services/               Application services and HTTP execution pipeline
  ForRest.Repositories/           Repository contracts
  ForRest.Infrastructure.Sqlite/  SQLite persistence and DPAPI-backed secret protection
  ForRest.Scripting/              Roslyn script host and script-facing APIs
  ForRest.Plugins.Abstractions/   Versionable extension contracts
  ForRest.Plugins.Host/           Discovery scaffold
  ForRest.Shared/                 Shared result helpers

tests/
  ForRest.Tests/                 Unit and repository tests
```

## Commands

```powershell
dotnet build ForRest.slnx
dotnet test tests\ForRest.Tests\ForRest.Tests.csproj
Start-Process .\src\ForRest.App\bin\Debug\net10.0-windows10.0.19041.0\ForRest.App.exe

# macOS unsigned build (no Apple Developer license required)
# Users run the .app via right-click → Open or Security & Privacy settings.
dotnet publish src/ForRest.Maui/ForRest.Maui.csproj -f net10.0-maccatalyst -c Release -p:ForRestMacUnsigned=true
```

## Architecture Rules

- Keep dependency flow downward only: `App -> Services -> Repositories -> Infrastructure`, with `Domain`, `Models`, `Scripting`, and plugin abstractions isolated from infrastructure concerns.
- Do not move business rules into views or XAML code-behind.
- Keep variable precedence explicit and test-covered.
- Keep scripting behind the host contract in `ForRest.Scripting`; do not leak UI or repository types into scripts.
- Treat secret values as secret by storage boundary, not by UI convention alone.
- Prefer permissive dependencies. Windows platform packages are accepted exceptions and are tracked in the dependency review docs.
- Preserve `file-scoped namespaces`, nullable enabled, primary constructors where they help DI-heavy classes, and `GlobalUsings.cs` per project.
- **Nullable reference types**: Enabled across all projects (`<Nullable>enable</Nullable>`)
- **File-scoped namespaces**: Always use `namespace Project.Name.Api.Functions;` (no braces)
- **Primary constructors**: Preferred for classes with DI
- **Collection expressions**: Use `[]` and `[..collection]`
- **Target-typed new**: Use `new()` when type is obvious
- **Global usings**: Define in `GlobalUsings.cs` per project

### Examples

**✅ Correct - Primary Constructor with File-Scoped Namespace:**
```csharp
using Project.Name.Server.Shared.Serialization;

namespace Project.Name.Api.Functions;

public class SomeModelFunctions(
    ISomeModelRepository someModelRepository,
    ILogger<SomeModelFunctions> logger)
{
    [Function("GetSomeModel")]
    public async Task<IActionResult> GetSomeModel(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "someModels/{someModelId}")] HttpRequestData req,
        FunctionContext context,
        [FromRoute] int someModelId)
    {
        var principal = context.GetPrincipal();
        if (principal is null) { return new UnauthorizedResult(); }

        var someModel = await someModelRepository.GetSomeModel(someModelId);
        return new JsonResult(someModel, SerializationOptions.Api);
    }
}
```

**❌ Incorrect - Traditional Constructor with Block-Scoped Namespace:**
```csharp
namespace Project.Name.Api.Functions
{
    public class SomeModelFunctions
    {
        private readonly ISomeModelRepository _someModelRepository;

        public SomeModelFunctions(ISomeModelRepository someModelRepository)
        {
            _someModelRepository = someModelRepository;
        }
    }
}
```

---

## Naming Conventions

### Variables and Fields
- **Local variables**: `camelCase`, use `var` when type is obvious
  ```csharp
  var someModel = await someModelRepository.GetSomeModel(someModelId);
  var data = new List<SomeModel>();
  ```
- **Fields**: `camelCase` (no underscore prefix)
  ```csharp
  private readonly ILogger logger;
  ```
- **Properties**: `PascalCase`
  ```csharp
  public string SomeModelName { get; set; }
  ```
- **Constants**: `PascalCase`
  ```csharp
  private const int MaxRetries = 3;
  ```

### Types
- **Interfaces**: Prefix with `I` and use `PascalCase`
  ```csharp
  public interface ISomeModelRepository { }
  ```
- **Classes**: `PascalCase`
  ```csharp
  public class SomeModelRepository { }
  ```
- **Enums**: Type and members both `PascalCase`
  ```csharp
  public enum EventProcessorType
  {
      Unknown,
      RedLion,
      Cas
  }
  ```

### Methods
- **Async methods**: Do NOT suffix with `Async` unless there are both sync/async versions
  ```csharp
  // ✅ Correct
  public async Task<SomeModel> GetSomeModel(int someModelId)

  // ❌ Incorrect (unless there's also a synchronous GetSomeModelAsync)
  public async Task<SomeModel> GetSomeModelAsync(int someModelId)
  ```
- **Method names**: Describe intent, not implementation
  ```csharp
  // ✅ Good
  public async Task<SomeModel> GetSomeModel(int someModelId)

  // ❌ Bad
  public async Task<SomeModel> QueryDatabaseForSomeModelById(int someModelId)
  ```

---

## Code Organization

### Regions (Required)

Always structure classes with regions. Include blank lines at start and end of region content:

```csharp
public class SomeModelRepository(ProjectNameSqlConfiguration configuration)
{
    #region Private Fields

    private readonly string connectionString = configuration.ConnectionString;

    #endregion

    #region Public Methods

    public async Task<SomeModel> GetSomeModel(int someModelId)
    {
        // Implementation
    }

    #endregion

    #region Private Methods

    private void ValidateSomeModel(SomeModel someModel)
    {
        // Implementation
    }

    #endregion
}
```

**Standard Region Order:**
1. `#region Private Fields`
2. `#region Constructors` (only if not using primary constructors)
3. `#region Properties`
4. `#region Public Methods` or `#region Interface Implementations`
5. `#region Private Methods` or `#region Helpers`

---

## Dependency Injection

### Constructor Injection (Primary Constructors)

**✅ Preferred - Primary Constructor:**
```csharp
public class SomeModelFunctions(
    ISomeModelRepository someModelRepository,
    ISomthingRepository somthingRepository,
    ILogger<SomeModelFunctions> logger)
{
    // Use parameters directly - no need to assign to fields
    public async Task<SomeModel> GetSomeModel(int someModelId)
    {
        logger.LogInformation("Getting someModel {SomeModelId}", someModelId);
        return await someModelRepository.GetSomeModel(someModelId);
    }
}
```

**✅ Acceptable - Traditional Constructor (for complex initialization):**
```csharp
public class SomeModelService
{
    #region Private Fields

    private readonly ISomeModelRepository someModelRepository;
    private readonly ILogger logger;

    #endregion

    #region Constructors

    public SomeModelService(ISomeModelRepository someModelRepository, ILogger<SomeModelService> logger)
    {
        this.someModelRepository = someModelRepository;
        this.logger = logger;
    }

    #endregion
}
```

**❌ Never Use:**
- Property injection
- Field injection
- Service locator pattern

### Lifetime Guidelines
- **Repositories**: `Scoped` (due to EF DbContext)
- **Services**: `Scoped` (default for stateless business logic)
- **Caches, SignalR**: `Singleton` (for stateful services)
- **Configuration**: `Singleton`

---

## Logging Standards

### Logger Injection (Required)

**All classes should inject `ILogger<T>` for diagnostics:**

```csharp
public class SomeModelFunctions(
    ISomeModelRepository someModelRepository,
    ILogger<SomeModelFunctions> logger) // ✅ Always inject logger
{
    [Function("GetSomeModel")]
    public async Task<IActionResult> GetSomeModel(...)
    {
        logger.LogInformation("Fetching someModel {SomeModelId}", someModelId);

        try
        {
            var someModel = await someModelRepository.GetSomeModel(someModelId);
            return new JsonResult(someModel, SerializationOptions.Api);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve someModel {SomeModelId}", someModelId);
            throw;
        }
    }
}
```

### Logging Best Practices

**✅ Use Structured Logging:**
```csharp
// ✅ Good - Structured with parameters
logger.LogError(ex, "Failed to ingest data to {DatabaseName}, table {TableName}",
    databaseName, tableName);

// ❌ Bad - String interpolation loses structure
logger.LogError(ex, $"Failed to ingest data to {databaseName}, table {tableName}");
```

**✅ Appropriate Log Levels:**
```csharp
logger.LogTrace("Entering GetSomeModel method");           // Verbose diagnostics
logger.LogDebug("Query returned {Count} someModels", count); // Debugging info
logger.LogInformation("SomeModel {SomeModelId} retrieved", id); // Normal flow
logger.LogWarning("SomeModel {SomeModelId} not found", id);     // Unexpected but handled
logger.LogError(ex, "Failed to query database");          // Errors
logger.LogCritical(ex, "Database connection failed");     // System failures
```

**❌ Never Use `Console.WriteLine` in Production Code:**
```csharp
// ❌ BAD - Not captured by Application Insights
catch (Exception ex)
{
    Console.WriteLine(ex.ToString());
}

// ✅ GOOD - Structured logging
catch (Exception ex)
{
    logger.LogError(ex, "Failed to process request");
}
```

---

## JSON Serialization

### Centralized Configuration (Required)

**Always use the centralized serialization options:**

```csharp
using Project.Name.Server.Shared.Serialization;

// ✅ Use centralized options
return new JsonResult(data, SerializationOptions.Api);

// ❌ Don't create local options
private readonly JsonSerializerOptions options = new() { ... };
```

**SerializationOptions.Api includes:**
- `PropertyNameCaseInsensitive = true`
- `ReferenceHandler = ReferenceHandler.IgnoreCycles` (handles circular references)

---

## Null Handling

### Null Checks

**✅ Preferred - Pattern Matching (`is null`):**
```csharp
if (someModel is null) { return new NotFoundResult(); }
if (principal is null) { return new UnauthorizedResult(); }
```

**⚠️ Acceptable - Equality (`== null`):**
```csharp
if (someModel == null) { return new NotFoundResult(); }
```

**Choose one pattern and use consistently within a file.**

### Null-Forgiving Operator

**⚠️ Use Sparingly - Only When You're Certain:**
```csharp
// ⚠️ Only use ! when you're absolutely certain it's not null
.ThenInclude(site => site!.Customer)

// ✅ Better - Add null check
.ThenInclude(site => site.Customer)
.Where(c => c.Site != null)
```

### Nullable Reference Types

**Always provide defaults for nullable properties:**
```csharp
// ✅ Good
public string Name { get; set; } = string.Empty;
public ICollection<SomeModel> SomeModels { get; set; } = [];

// ❌ Bad - nullable warning
public string Name { get; set; }
```

---

## Entity Framework Patterns

### Query Optimization (Required Pattern)

**Standard EF Query Pattern:**
```csharp
return await Query<SomeModel>()
    .Include(someModel => someModel.SomeModelType)           // 1. Eager load relationships
    .Include(someModel => someModel.Tags)                 // 2. All includes first
    .ThenInclude(tag => tag.Somes)          // 3. Then nested includes
    .Where(someModel => someModel.SiteId == siteId)       // 4. Filter
    .AsNoTracking()                                 // 5. Read-only optimization
    .AsSplitQuery()                                 // 6. Performance for multiple collections
    .ToListAsync();                                 // 7. Execute
```

**Order matters for readability - always use this sequence:**
1. `Include()` / `ThenInclude()`
2. `Where()` filters
3. `OrderBy()` / `OrderByDescending()`
4. `AsNoTracking()` (for read-only queries)
5. `AsSplitQuery()` (when including multiple collections)
6. `ToListAsync()` / `FirstOrDefaultAsync()` / etc.

---

## Async/Await Patterns

### Best Practices

**✅ Always await async calls:**
```csharp
var someModel = await someModelRepository.GetSomeModel(someModelId);
```

**❌ Never block async calls:**
```csharp
// ❌ BAD - Blocking
var someModel = someModelRepository.GetSomeModel(someModelId).Result;
var someModels = someModelRepository.GetSomeModels().Wait();

// ✅ GOOD
var someModel = await someModelRepository.GetSomeModel(someModelId);
await someModelRepository.Save(someModel);
```

**✅ Return Task directly when possible:**
```csharp
// ✅ Good - no unnecessary async/await
public Task<SomeModel> GetSomeModel(int someModelId)
{
    return someModelRepository.GetSomeModel(someModelId);
}

// ⚠️ Only use async/await when you need to manipulate the result
public async Task<SomeModel> GetSomeModel(int someModelId)
{
    var someModel = await someModelRepository.GetSomeModel(someModelId);
    logger.LogInformation("Retrieved someModel {SomeModelId}", someModelId);
    return someModel;
}
```

---

## Collections

### Modern Collection Syntax

**✅ Use collection expressions:**
```csharp
// ✅ Empty collection
var someModels = new List<SomeModel>();
// or
List<SomeModel> someModels = [];

// ✅ Collection from enumerable
var someModelList = [..someModels.Where(d => d.Active)];

// ✅ Initialization
var tags = new List<string> { "tag1", "tag2" };
// or
List<string> tags = ["tag1", "tag2"];
```

**✅ Property initialization:**
```csharp
public class SomeModel
{
    // ✅ Initialize collections to prevent nulls
    public ICollection<SomeModelTag> Tags { get; set; } = [];
    public ICollection<Pump> Pumps { get; set; } = [];
}
```

---

## Architecture Patterns

### Project References

**Understand the dependency flow:**

```
┌─────────────────────────┐
│   WinUI                 │  ← Presentation Layer
└───────────┬─────────────┘
            │
┌───────────▼─────────────┐
│  Services / Processors  │  ← Business Logic
└───────────┬─────────────┘
            │
┌───────────▼─────────────┐
│     Repositories        │  ← Data Access 
└───────────┬─────────────┘
            │
┌───────────▼─────────────┐
│ Infrastructure (Storage)│  ← Infrastructure (to SQLITE only (local only))
│                         │
└───────────┬─────────────┘
            │
┌───────────▼─────────────┐
│   Domain / Models       │  ← Core Domain
└─────────────────────────┘
```

**Never reference upward in this hierarchy** (e.g., Domain should not reference Infrastructure)

### When to Create New Files

- **Entity**: `src/Project.Name.Domain/Entities/`
- **Repository Interface**: `src/Project.Name.Repositories/Interfaces/`
- **Repository Implementation**: `src/Project.Name.Infrastructure.EF/Repositories/`
- **Service**: `src/Project.Name.Services/`
- **API Function**: `src/Project.Name.Api/Functions/`
- **Shared Models**: `src/Project.Name.Models/`

---

## Anti-Patterns (Never Do This)

### ❌ Console Output in Production
```csharp
// ❌ NEVER
Console.WriteLine("Error occurred");
Console.WriteLine(ex.ToString());

// ✅ ALWAYS use ILogger
logger.LogError(ex, "Error occurred");
```

### ❌ Duplicate Configuration
```csharp
// ❌ Don't duplicate JsonSerializerOptions
private readonly JsonSerializerOptions options = new() { ... };

// ✅ Use centralized
return new JsonResult(data, SerializationOptions.Api);
```

### ❌ Blocking Async Code
```csharp
// ❌ NEVER
var result = asyncMethod().Result;
asyncMethod().Wait();

// ✅ ALWAYS
var result = await asyncMethod();
await asyncMethod();
```

### ❌ Swallowing Exceptions
```csharp
// ❌ BAD - Silent failure
try
{
    await ProcessData();
}
catch { }

// ✅ GOOD - Log and handle appropriately
try
{
    await ProcessData();
}
catch (Exception ex)
{
    logger.LogError(ex, "Failed to process data");
    throw; // or handle appropriately
}
```

### ❌ String Concatenation in Logging
```csharp
// ❌ BAD - Loses structured logging
logger.LogError($"Failed for someModel {someModelId}");

// ✅ GOOD - Structured parameters
logger.LogError("Failed for someModel {SomeModelId}", someModelId);
```

---

## Code Review Checklist

Before submitting code, verify:

- [ ] **Nullable reference types**: No nullable warnings
- [ ] **Logging**: ILogger injected and used (no Console.WriteLine)
- [ ] **Serialization**: Using `SerializationOptions.Api`
- [ ] **Null checks**: Using `is null` consistently
- [ ] **Async/await**: No `.Result` or `.Wait()` calls
- [ ] **EF queries**: Following standard pattern (Include → Where → AsNoTracking → AsSplitQuery)
- [ ] **Regions**: Code organized with proper regions
- [ ] **Primary constructors**: Used when appropriate
- [ ] **File-scoped namespaces**: Using `namespace X;` not `namespace X { }`
- [ ] **Collection initialization**: Properties initialized to `[]`


---

## Progress Tracking

## Known Gaps After This Commit

## Next Priorities
