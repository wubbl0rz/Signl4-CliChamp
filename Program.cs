using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Cocona;
using Humanizer;
using Humanizer.Localisation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PunchChamp;
using Spectre.Console;

var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var configPath = Path.Combine(homeDirectory, ".config", "punchchamp");
var configFile = Path.Combine(configPath, "punchchamp.json");
Directory.CreateDirectory(configPath);

// CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
//
// Console.WriteLine(DateTime.Parse("13.02.2024 11:11:11"));
//
// return;
// //Console.WriteLine(DateTime.Parse("13.02.2024 11:11:11"));

var builder = CoconaApp.CreateBuilder();

//todo: rename environment user
builder.Configuration
  .AddJsonFile(configFile, true)
  .AddEnvironmentVariables("PunchChamp_");

builder.Services.AddSingleton(builder.Configuration.Get<AppConfig>());

builder.Services.AddTransient<Signl4Api>();

var app = builder.Build();

app.AddCommand("watch", async (string? user,
  Signl4Api api,
  AppConfig config,
  CoconaAppContext ctx) =>
{
  user ??= config.User;

  ArgumentException.ThrowIfNullOrWhiteSpace(user);

  var userData = await api.GetUserData(user);

  var alert = await api.GetLatestAlert(userData.Id, userData.TeamId);

  await AnsiConsole.Progress()
    .AutoClear(true)
    .Columns(new SpinnerColumn(Spinner.Known.Dots12), new TaskDescriptionColumn())
    .StartAsync(async progress =>
    {
      progress.AddTask("Waiting for alerts...");

      while (!ctx.CancellationToken.IsCancellationRequested)
      {
        var latestAlert = await api.GetLatestAlert(userData.Id, userData.TeamId);

        if (latestAlert != null && alert?.Id != latestAlert.Id)
        {
          var table = new Table();

          table.AddColumn($"\ud83d\udea8 {latestAlert.Title}");
          table.AddRow(latestAlert.Text);

          AnsiConsole.Write(table);

          var url = $"https://account.signl4.com/manage/Signls?alertId={latestAlert.Id}";

          //File.Exists("/usr/bin/notify-send");

          if (config.Notify)
          {
            try
            {
              Process.Start("notify-send",
                $"\"\ud83d\udea8 {latestAlert.Title}\" \"{latestAlert.Text}\n<a href='{url}'>link</a>\"");
            }
            catch (Exception _)
            {
              // ignored
            }
          }
        }

        alert = latestAlert;

        await Task.Delay(5000, ctx.CancellationToken).ContinueWith(_ => { });
      }
    });
});

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

    //todo: retry api connection
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
  public bool Notify { get; set; } = true;
  public string? User { get; set; }
  public string? Key { get; set; }
}

record AlertDataArray(AlertData[] Results);

record AlertData(string Id, string Title, string Text, uint Severity);

record AlertFilter(string[] TeamIds, string TextToSearch);