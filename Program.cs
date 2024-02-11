using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cocona;
using Humanizer;
using Humanizer.Localisation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var configPath = Path.Combine(homeDirectory, ".config", "punchchamp");
var configFile = Path.Combine(configPath, "punchchamp.json");
Directory.CreateDirectory(configPath);

var builder = CoconaApp.CreateBuilder();

builder.Configuration
  .AddJsonFile(configFile, false)
  .AddEnvironmentVariables("PunchChamp_");

builder.Services.AddSingleton(builder.Configuration.Get<AppConfig>());

builder.Services.AddTransient<Signl4Api>();

var app = builder.Build();

app.AddCommand("info", async (string? user,
  Signl4Api api,
  AppConfig config) =>
{
  user ??= config.User;

  ArgumentException.ThrowIfNullOrWhiteSpace(user);

  var userData = await api.GetUserData(user);

  if (userData!.IsPunchedIn)
  {
    AnsiConsole.WriteLine($"🚀 User \"{userData.DisplayName}\" is punched in.");
    AnsiConsole.WriteLine($"\u23f0 Started: {userData.LastChange:HH:mm:ss [dd.MM.yyyy]}");

    var activeDuration = DateTime.Now - userData.LastChange;

    //todo: duration in rot bei über 8h,warnung bei über 8h :p
    //todo: punchout notifcation count. un bestätige notificatuions.
    //todo: notify send notify linux when alert
    AnsiConsole.WriteLine($"\u23f3 Duration: {activeDuration.Humanize(3, minUnit: TimeUnit.Second)}");
    return;
  }

  AnsiConsole.WriteLine($"\ud83d\udecc User \"{userData.DisplayName}\" is punched out.");
  AnsiConsole.WriteLine($"\u23f0 Ended: {userData.LastChange:HH:mm:ss [dd.MM.yyyy]}");
});

app.AddCommand("punch-in", async (string? user,
  DateTime? till,
  CoconaAppContext ctx,
  Signl4Api api,
  AppConfig appConfig) =>
{
  user ??= appConfig.User;

  ArgumentException.ThrowIfNullOrWhiteSpace(user);

  var userData = await api.GetUserData(user);
  var punchInTime = userData.LastChange;

  if (userData!.IsPunchedIn)
  {
    AnsiConsole.WriteLine($"\ud83d\udc40 User \"{userData.DisplayName}\" is already punched in.");
    AnsiConsole.WriteLine($"\u23f0 Started: {userData.LastChange:HH:mm:ss [dd.MM.yyyy]} " +
                          $"({userData.LastChange.Humanize()})");
  }
  else
  {
    await api.PunchIn(userData.Id, userData.TeamId);
    punchInTime = DateTime.Now;

    AnsiConsole.WriteLine($"🚀 User \"{userData.DisplayName}\" is now punched in.");
    AnsiConsole.WriteLine($"\u23f0 Started: {DateTime.Now:HH:mm:ss [dd.MM.yyyy]}");
  }

  if (till == null)
    return;

  await AnsiConsole.Progress()
    .AutoClear(true)
    .Columns(new SpinnerColumn(Spinner.Known.Dots12), new TaskDescriptionColumn())
    .StartAsync(async progress =>
    {
      var task = progress.AddTask("-");

      while (!ctx.CancellationToken.IsCancellationRequested)
      {
        var timeLeft = DateTime.Now - till;

        if (DateTime.Now >= till)
        {
          await api.PunchOut(userData.Id, userData.TeamId);

          var activeDuration = DateTime.Now - punchInTime;

          AnsiConsole.WriteLine($"\ud83d\udca6 Shift ended after: " +
                                $"{activeDuration.Humanize(3, minUnit: TimeUnit.Second)}");
          AnsiConsole.WriteLine($"\ud83d\udecc User \"{userData.DisplayName}\" is now punched out.");

          break;
        }

        task.Description($"{timeLeft!.Value.Humanize(3, minUnit: TimeUnit.Second)} left.");

        await Task.Delay(1000, ctx.CancellationToken).ContinueWith(_ => { });
      }
    });
});

app.AddCommand("punch-out", async (string? user, Signl4Api api,
  AppConfig appConfig) =>
{
  user ??= appConfig.User;

  ArgumentException.ThrowIfNullOrWhiteSpace(user);

  var userData = await api.GetUserData(user);

  if (userData.IsPunchedIn)
  {
    await api.PunchOut(userData.Id, userData.TeamId);

    AnsiConsole.WriteLine($"\ud83d\udecc User \"{userData.DisplayName}\" is now punched out.");
    var activeDuration = DateTime.Now - userData.LastChange;

    AnsiConsole.WriteLine($"\ud83d\udca6 Shift ended after: " +
                          $"{activeDuration.Humanize(3, minUnit: TimeUnit.Second)}");
    return;
  }

  AnsiConsole.WriteLine($"\ud83d\udecc User \"{userData.DisplayName}\" is not punched in.");
});

app.Run();

record UserData(string Id, string Name, string Mail, DutyInfo[] DutyInfos)
{
  public string TeamId => this.DutyInfos[0].TeamId;
  public bool IsPunchedIn => this.DutyInfos[0].DutyMode != DutyMode.None;
  public DateTime LastChange => this.DutyInfos[0].LastChange.LocalDateTime;
  public string DisplayName => string.IsNullOrWhiteSpace(this.Name) ? this.Mail : this.Name;
}

record PunchInOutData(string UserId, string[] TeamIds);

record DutyInfo(DateTimeOffset LastChange, string TeamId, DutyMode DutyMode, string Name, string Mail);

enum DutyMode
{
  None = 0,
  Primary = 1,
  Manager = 100,
}

record AppConfig
{
  public string? User { get; set; }
  public string? Key { get; set; }
}

class Signl4Api
{
  private readonly HttpClient _httpClient;
  private readonly string baseUrl = "https://connect.signl4.com/api/v2";

  public Signl4Api(AppConfig appConfig)
  {
    var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.Add("x-s4-api-key", appConfig.Key);
    _httpClient = httpClient;
  }

  public async Task<UserData> GetUserData(string user)
  {
    try
    {
      var userData = await _httpClient.GetFromJsonAsync<UserData>($"{baseUrl}/users/{user}");
      return userData ?? throw new InvalidOperationException();
    }
    catch (Exception _)
    {
      throw new Exception($"User: {user} not found.");
    }
  }

  public async Task PunchIn(string userId, string teamId)
  {
    var punchInOutData = new PunchInOutData(userId, [teamId]);

    await _httpClient.PostAsJsonAsync($"{baseUrl}/duties/punchIn", punchInOutData);
  }

  public async Task PunchOut(string userId, string teamId)
  {
    var punchInOutData = new PunchInOutData(userId, [teamId]);

    var result = await _httpClient
      .PostAsJsonAsync($"{baseUrl}/duties/punchOut", punchInOutData);
  }
}