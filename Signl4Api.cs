using System.Net.Http.Json;

namespace PunchChamp;

class Signl4Api
{
  private readonly HttpClient _httpClient;
  private readonly string _baseUrl = "https://connect.signl4.com/api/v2";

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
      var userData = await _httpClient.GetFromJsonAsync<UserData>($"{_baseUrl}/users/{user}");
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

    await _httpClient.PostAsJsonAsync($"{_baseUrl}/duties/punchIn", punchInOutData);
  }

  public async Task PunchOut(string userId, string teamId)
  {
    var punchInOutData = new PunchInOutData(userId, [teamId]);

    var result = await _httpClient
      .PostAsJsonAsync($"{_baseUrl}/duties/punchOut", punchInOutData);
  }

  public async Task<AlertData?> GetLatestAlert(string userId, string teamId, string searchText = "")
  {
    var filter = new AlertFilter([teamId], searchText);

    var result = await _httpClient
      .PostAsJsonAsync($"{_baseUrl}/alerts/paged?userId={userId}&maxResults=1", filter);

    var alerts = await result.Content.ReadFromJsonAsync<AlertDataArray>();

    return alerts?.Results.First();
  }
}