using Godot;
using System.Threading.Tasks;

[GlobalClass]
public partial class BackendService : Node
{
	[Signal] public delegate void ConnectionStatusChangedEventHandler(bool isConnected);
	[Signal] public delegate void DataSyncCompletedEventHandler(bool success);

	[ExportCategory("Backend Configuration")]
	[Export] public string ApiBaseUrl { get; set; } = "http://localhost:8080/api";
	[Export] public float ConnectionTimeoutSeconds { get; set; } = 10.0f;
	[Export] public bool EnableOfflineMode { get; set; } = true;

	private HttpRequest _httpRequest;
	private bool _isConnected = false;
	private Timer _connectionCheckTimer;

	public override void _Ready()
	{
		SetupHttpRequest();
		SetupConnectionTimer();
		CheckConnection();
	}

	private void SetupHttpRequest()
	{
		_httpRequest = new HttpRequest();
		_httpRequest.Timeout = ConnectionTimeoutSeconds;
		AddChild(_httpRequest);
	}

	private void SetupConnectionTimer()
	{
		_connectionCheckTimer = new Timer();
		_connectionCheckTimer.WaitTime = 30.0f; // Check every 30 seconds
		_connectionCheckTimer.Timeout += CheckConnection;
		AddChild(_connectionCheckTimer);
		_connectionCheckTimer.Start();
	}

	private void CheckConnection()
	{
		// Placeholder for future backend connectivity check
		// For now, assume offline mode
		SetConnectionStatus(false);
	}

	private void SetConnectionStatus(bool connected)
	{
		if (_isConnected != connected)
		{
			_isConnected = connected;
			EmitSignal(SignalName.ConnectionStatusChanged, _isConnected);
			
			if (_isConnected)
			{
				GD.Print("Backend service connected");
				_ = SyncUserData();
			}
			else if (EnableOfflineMode)
			{
				GD.Print("Backend service offline - using local data");
			}
		}
	}

	public async Task<bool> SyncUserData()
	{
		if (!_isConnected)
		{
			return false;
		}

		// Placeholder for future user data synchronization
		await Task.Delay(100);
		
		EmitSignal(SignalName.DataSyncCompleted, true);
		return true;
	}

	public async Task<bool> UploadHighScore(string gameId, int score, string userId)
	{
		if (!_isConnected)
		{
			return false;
		}

		// Placeholder for future high score upload
		await Task.Delay(100);
		
		GD.Print($"High score uploaded: {gameId} - {score} by {userId}");
		return true;
	}

	public async Task<int[]> GetLeaderboard(string gameId, int limit = 10)
	{
		if (!_isConnected)
		{
			return new int[0];
		}

		// Placeholder for future leaderboard retrieval
		await Task.Delay(100);
		
		return new int[0];
	}

	public bool IsConnected()
	{
		return _isConnected;
	}

	public bool IsOfflineModeEnabled()
	{
		return EnableOfflineMode;
	}
}