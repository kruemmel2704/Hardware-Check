namespace HardwareCheck.Api.Services;

public class RecordingBackgroundWorker : BackgroundService
{
    private readonly LhmDiscoveryAndMonitorService _monitorService;
    private readonly HardwareRecordingService _recordingService;

    public RecordingBackgroundWorker(
        LhmDiscoveryAndMonitorService monitorService,
        HardwareRecordingService recordingService)
    {
        _monitorService = monitorService;
        _recordingService = recordingService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!_recordingService.IsRecording) continue;
            try
            {
                await _monitorService.GetDashboardDataAsync(stoppingToken);
            }
            catch
            {
                // Ignore background poll error
            }
        }
    }
}

