using System.Security.Cryptography;
using Microsoft.Playwright;

namespace TaskFlow.DevCli;

internal sealed class BrowserE2eCommands
{
    private readonly ComposeEnvironment _environment;

    public BrowserE2eCommands(ComposeEnvironment environment)
    {
        _environment = environment;
    }

    public static int InstallChromium(bool withDependencies)
    {
        var arguments = withDependencies
            ? new[] { "install", "--with-deps", "chromium" }
            : new[] { "install", "chromium" };
        var exitCode = Microsoft.Playwright.Program.Main(arguments);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Playwright browser installation failed with exit code {exitCode}.");
        }

        Console.WriteLine("Playwright Chromium is installed.");
        return 0;
    }

    public async Task<int> RunAsync(bool installBrowser)
    {
        if (installBrowser)
        {
            var withDependencies = OperatingSystem.IsLinux() &&
                string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
            InstallChromium(withDependencies);
        }

        var values = _environment.Read();
        var port = values.GetValueOrDefault("TASKFLOW_HTTPS_PORT", "8443");
        var baseUrl = $"https://localhost:{port}";
        var runId = $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}-{RandomHex(3)}";
        var username = $"browser-{runId}";
        var password = $"TaskFlow!Aa1{RandomToken(18)}";
        var projectName = $"Browser project {runId}";
        var projectEdited = $"Browser project edited {runId}";
        var taskName = $"Browser task {runId}";
        var taskEdited = $"Browser task edited {runId}";
        var tagName = $"browser-{RandomHex(4)}";

        using var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        }).ConfigureAwait(false);

        try
        {
            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                IgnoreHTTPSErrors = true,
            }).ConfigureAwait(false);
            var page = await context.NewPageAsync().ConfigureAwait(false);
            page.SetDefaultTimeout(20_000);

            await RegisterAsync(page, baseUrl, username, password).ConfigureAwait(false);
            await LoginRoundTripAsync(page, username, password).ConfigureAwait(false);

            await page.GetByRole(AriaRole.Button, new() { Name = "New project" }).ClickAsync().ConfigureAwait(false);
            await FillProjectFormAsync(page, projectName, "Created by .NET browser E2E", "project-create-form").ConfigureAwait(false);
            await page.GetByTestId("project-create-form").GetByRole(AriaRole.Button, new() { Name = "Create project" }).ClickAsync().ConfigureAwait(false);
            await page.GetByRole(AriaRole.Heading, new() { Name = projectName }).WaitForAsync().ConfigureAwait(false);
            var urlSegments = page.Url.TrimEnd('/').Split('/');
            var projectId = urlSegments[^1];
            Require(Guid.TryParse(projectId, out _), $"Browser E2E did not navigate to a project UUID: {page.Url}");

            await page.GetByRole(AriaRole.Link, new() { Name = "Edit project" }).ClickAsync().ConfigureAwait(false);
            await FillProjectFormAsync(page, projectEdited, "Edited by .NET browser E2E", "project-edit-form").ConfigureAwait(false);
            await page.GetByTestId("project-edit-form").GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync().ConfigureAwait(false);
            await page.GetByRole(AriaRole.Heading, new() { Name = projectEdited }).WaitForAsync().ConfigureAwait(false);

            await page.GetByRole(AriaRole.Link, new() { Name = "New task" }).ClickAsync().ConfigureAwait(false);
            var taskCreate = page.GetByTestId("task-create-form");
            await taskCreate.GetByLabel("Title").FillAsync(taskName).ConfigureAwait(false);
            await taskCreate.GetByLabel("Description").FillAsync("Created by .NET browser E2E").ConfigureAwait(false);
            await taskCreate.GetByLabel("Status").SelectOptionAsync("Todo").ConfigureAwait(false);
            await taskCreate.GetByLabel("Priority").SelectOptionAsync("High").ConfigureAwait(false);
            await taskCreate.GetByRole(AriaRole.Button, new() { Name = "Create task" }).ClickAsync().ConfigureAwait(false);
            var taskRow = await RowWithTextAsync(page, "task-list", taskName).ConfigureAwait(false);

            await taskRow.GetByRole(AriaRole.Link, new() { Name = "Edit" }).ClickAsync().ConfigureAwait(false);
            var taskEdit = page.GetByTestId("task-edit-form");
            await taskEdit.GetByLabel("Title").FillAsync(taskEdited).ConfigureAwait(false);
            await taskEdit.GetByLabel("Description").FillAsync("Edited by .NET browser E2E").ConfigureAwait(false);
            await taskEdit.GetByLabel("Status").SelectOptionAsync("InProgress").ConfigureAwait(false);
            await taskEdit.GetByLabel("Priority").SelectOptionAsync("Medium").ConfigureAwait(false);
            await taskEdit.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync().ConfigureAwait(false);
            await RowWithTextAsync(page, "task-list", taskEdited).ConfigureAwait(false);

            await page.GetByRole(AriaRole.Link, new() { Name = "Tags" }).ClickAsync().ConfigureAwait(false);
            await page.GetByRole(AriaRole.Button, new() { Name = "New tag" }).ClickAsync().ConfigureAwait(false);
            var tagCreate = page.GetByTestId("tag-create-form");
            await tagCreate.GetByLabel("Name").FillAsync(tagName).ConfigureAwait(false);
            await tagCreate.GetByRole(AriaRole.Button, new() { Name = "Create tag" }).ClickAsync().ConfigureAwait(false);
            await RowWithTextAsync(page, "tags-list", tagName).ConfigureAwait(false);

            await page.GetByRole(AriaRole.Link, new() { Name = "Projects" }).ClickAsync().ConfigureAwait(false);
            await page.GetByRole(AriaRole.Link, new() { Name = projectEdited, Exact = true }).ClickAsync().ConfigureAwait(false);
            taskRow = await RowWithTextAsync(page, "task-list", taskEdited).ConfigureAwait(false);
            await taskRow.Locator("select.input-compact").SelectOptionAsync(new SelectOptionValue { Label = tagName }).ConfigureAwait(false);
            await taskRow.GetByRole(AriaRole.Button, new() { Name = "Attach" }).ClickAsync().ConfigureAwait(false);
            await page.GetByText("Tag attached.", new() { Exact = false }).WaitForAsync().ConfigureAwait(false);

            var filters = page.GetByTestId("task-filters");
            await filters.GetByLabel("Search", new() { Exact = true }).FillAsync(taskEdited).ConfigureAwait(false);
            await filters.GetByLabel("Status", new() { Exact = true }).SelectOptionAsync("InProgress").ConfigureAwait(false);
            await filters.GetByLabel("Priority", new() { Exact = true }).SelectOptionAsync("Medium").ConfigureAwait(false);
            await filters.GetByLabel("Tag", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = tagName }).ConfigureAwait(false);
            await filters.GetByLabel("Sort", new() { Exact = true }).SelectOptionAsync("title:asc").ConfigureAwait(false);
            await filters.GetByRole(AriaRole.Button, new() { Name = "Apply filters" }).ClickAsync().ConfigureAwait(false);
            await RowWithTextAsync(page, "task-list", taskEdited).ConfigureAwait(false);

            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle }).ConfigureAwait(false);
            await page.GetByRole(AriaRole.Heading, new() { Name = projectEdited }).WaitForAsync().ConfigureAwait(false);
            await page.GetByRole(AriaRole.Button, new() { Name = "Logout" }).WaitForAsync().ConfigureAwait(false);

            taskRow = await RowWithTextAsync(page, "task-list", taskEdited).ConfigureAwait(false);
            await taskRow.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync().ConfigureAwait(false);
            await taskRow.GetByRole(AriaRole.Button, new() { Name = "Confirm" }).ClickAsync().ConfigureAwait(false);
            await page.GetByText("No matching tasks", new() { Exact = false }).WaitForAsync().ConfigureAwait(false);

            await page.GetByRole(AriaRole.Link, new() { Name = "Tags" }).ClickAsync().ConfigureAwait(false);
            var tagRow = await RowWithTextAsync(page, "tags-list", tagName).ConfigureAwait(false);
            await tagRow.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync().ConfigureAwait(false);
            await tagRow.GetByRole(AriaRole.Button, new() { Name = "Confirm delete" }).ClickAsync().ConfigureAwait(false);

            await page.GetByRole(AriaRole.Link, new() { Name = "Projects" }).ClickAsync().ConfigureAwait(false);
            await page.GetByRole(AriaRole.Link, new() { Name = projectEdited, Exact = true }).ClickAsync().ConfigureAwait(false);
            await page.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync().ConfigureAwait(false);
            await page.GetByTestId("project-delete-confirmation").GetByRole(AriaRole.Button, new() { Name = "Confirm delete" }).ClickAsync().ConfigureAwait(false);
            await page.GetByRole(AriaRole.Heading, new() { Name = "Projects" }).WaitForAsync().ConfigureAwait(false);

            await page.GetByRole(AriaRole.Button, new() { Name = "Logout" }).ClickAsync().ConfigureAwait(false);
            await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).WaitForAsync().ConfigureAwait(false);

            await context.CloseAsync().ConfigureAwait(false);
            Console.WriteLine(".NET Playwright browser E2E passed: register/login, Project/Task/Tag CRUD, tag attach/filter, refresh/session and logout.");
            return 0;
        }
        finally
        {
            await browser.CloseAsync().ConfigureAwait(false);
        }
    }

    private static async Task RegisterAsync(IPage page, string baseUrl, string username, string password)
    {
        await page.GotoAsync($"{baseUrl}/auth/register", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle }).ConfigureAwait(false);
        await page.GetByLabel("User name").FillAsync(username).ConfigureAwait(false);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(password).ConfigureAwait(false);
        await page.GetByLabel("Confirm password").FillAsync(password).ConfigureAwait(false);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync().ConfigureAwait(false);
        await page.GetByRole(AriaRole.Heading, new() { Name = "Projects" }).WaitForAsync().ConfigureAwait(false);
    }

    private static async Task LoginRoundTripAsync(IPage page, string username, string password)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Logout" }).ClickAsync().ConfigureAwait(false);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).WaitForAsync().ConfigureAwait(false);
        await page.GetByLabel("User name").FillAsync(username).ConfigureAwait(false);
        await page.GetByLabel("Password").FillAsync(password).ConfigureAwait(false);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync().ConfigureAwait(false);
        await page.GetByRole(AriaRole.Heading, new() { Name = "Projects" }).WaitForAsync().ConfigureAwait(false);
    }

    private static async Task FillProjectFormAsync(IPage page, string name, string description, string testId)
    {
        var panel = page.GetByTestId(testId);
        await panel.GetByLabel("Name").FillAsync(name).ConfigureAwait(false);
        await panel.GetByLabel("Description").FillAsync(description).ConfigureAwait(false);
    }

    private static async Task<ILocator> RowWithTextAsync(IPage page, string testId, string text)
    {
        var row = page.GetByTestId(testId).Locator("tbody tr", new LocatorLocatorOptions { HasText = text });
        await row.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15_000,
        }).ConfigureAwait(false);
        return row;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static string RandomHex(int byteCount) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(byteCount)).ToLowerInvariant();

    private static string RandomToken(int byteCount)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteCount));
        return token.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
