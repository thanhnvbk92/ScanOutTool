using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScanOutTool.Services;
using ScanOutTool.Services.Orchestration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.IO;
using System.Media;

namespace ScanOutTool.ViewModels
{
    /// <summary>
    /// Refactored ViewModel - chỉ quản lý UI state và binding, business logic đã tách ra ScanWorkflowService
    /// </summary>
    public partial class DashboardViewModel : ObservableObject, IDisposable
    {
        public enum RunMode
        {
            ScanOutOnly,
            RescanOnly,
            ScanOut_Rescan,
            None
        }

        private readonly IScanWorkflowService _scanWorkflowService;
        private readonly ILoggingService _loggingService;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private bool _disposed;

        // UI State Properties
        [ObservableProperty] private bool _isSessionStarting;
        [ObservableProperty] private bool _isDataSending;
        [ObservableProperty] private string _logs = string.Empty;
        [ObservableProperty] private string _startBtnText = "START";
        [ObservableProperty] private string _pID = string.Empty;
        [ObservableProperty] private string _partNo = string.Empty;
        [ObservableProperty] private string _workOrder = string.Empty;
        [ObservableProperty] private string _result = string.Empty;
        [ObservableProperty] private string _resultMessage = string.Empty;
        [ObservableProperty] private string _pCBLocation = string.Empty;
        [ObservableProperty] private RunMode _selectedRunMode = RunMode.ScanOut_Rescan;
        [ObservableProperty] private List<RunMode> _runModes;
        [ObservableProperty] private string _informationMessage = string.Empty;
        [ObservableProperty] private bool _isMessageOn;
        [ObservableProperty] private int _magazineQty;

        private bool _isStarted;
        public bool IsStarted
        {
            get => _isStarted;
            set
            {
                SetProperty(ref _isStarted, value);
                UpdateButtonState();
            }
        }

        // ✅ NEW: Property to control button visibility and text
        [ObservableProperty] private bool _isStartButtonVisible = true;
        [ObservableProperty] private bool _isStartButtonEnabled = true;

        private void UpdateButtonState()
        {
            if (IsSessionStarting)
            {
                StartBtnText = "STARTING...";
                IsStartButtonVisible = false; // ✅ Hide button during startup
                IsStartButtonEnabled = false;
            }
            else if (_isStarted)
            {
                StartBtnText = "STOP";
                IsStartButtonVisible = true;
                IsStartButtonEnabled = true;
            }
            else
            {
                StartBtnText = "START";
                IsStartButtonVisible = true;
                IsStartButtonEnabled = true;
            }
        }

        public DashboardViewModel(
            IScanWorkflowService scanWorkflowService,
            ILoggingService loggingService)
        {
            _scanWorkflowService = scanWorkflowService ?? throw new ArgumentNullException(nameof(scanWorkflowService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            InitializeViewModel();
            SubscribeToEvents();
        }

        private void InitializeViewModel()
        {
            RunModes = Enum.GetValues<RunMode>().ToList();
            SelectedRunMode = RunMode.ScanOut_Rescan;
            IsStarted = false;
            StartBtnText = "START";
            
            _loggingService.LogInformation("DASHBOARD: DashboardViewModel initialized");
        }

        private void SubscribeToEvents()
        {
            _scanWorkflowService.StatusChanged += OnWorkflowStatusChanged;
            _scanWorkflowService.ScanDataReceived += OnScanDataReceived;
            _scanWorkflowService.ErrorOccurred += OnWorkflowErrorOccurred;
            _loggingService.OnNewLog += OnNewLogEntry;
        }

        [RelayCommand]
        private async Task StartAsync()
        {
            if (IsStarted)
            {
                await StopWorkflowAsync().ConfigureAwait(false);
            }
            else
            {
                await StartWorkflowAsync().ConfigureAwait(false);
            }
        }

        private async Task StartWorkflowAsync()
        {
            CancellationTokenSource? timeoutCts = null;
            try
            {
                IsSessionStarting = true;
                UpdateButtonState();
                _loggingService.LogInformation("USER ACTION: Start button clicked - initiating workflow startup");

                // ✅ NEW: Add timeout to prevent hanging
                timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _cancellationTokenSource.Token, timeoutCts.Token);

                _loggingService.LogInformation("DASHBOARD: Calling ScanWorkflowService.StartAsync...");
                
                var result = await _scanWorkflowService
                    .StartAsync(linkedCts.Token)
                    .ConfigureAwait(false);

                _loggingService.LogInformation("DASHBOARD: ScanWorkflowService.StartAsync completed with result: " + result.Success);

                if (result.Success)
                {
                    IsStarted = true;
                    _loggingService.LogInformation("DASHBOARD: Workflow startup completed successfully");
                    _loggingService.LogInformation("DASHBOARD: System is now ready to process scan data");
                    
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        UpdateButtonState();
                    });
                }
                else
                {
                    _loggingService.LogError("DASHBOARD: Workflow startup FAILED");
                    _loggingService.LogError("DASHBOARD: Failure reason: " + result.Message);
                    if (result.Exception != null)
                    {
                        _loggingService.LogError("DASHBOARD: Exception details logged in service layer");
                    }
                    
                    _loggingService.LogError("DASHBOARD: Please check the following:");
                    _loggingService.LogError("   COM ports configuration in Settings");
                    _loggingService.LogError("   ScanOut application is running");
                    _loggingService.LogError("   PLC connection (if enabled)");
                    _loggingService.LogError("   Network connectivity");
                    
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        IsStarted = false;
                        UpdateButtonState();
                    });
                }
            }
            catch (OperationCanceledException) when (timeoutCts?.Token.IsCancellationRequested == true)
            {
                _loggingService.LogError("DASHBOARD: Workflow startup TIMEOUT after 30 seconds");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsStarted = false;
                    UpdateButtonState();
                });
            }
            catch (Exception ex)
            {
                _loggingService.LogError("DASHBOARD: Unexpected error during workflow startup");
                _loggingService.LogError("DASHBOARD: Error: " + ex.Message);
                _loggingService.LogError("DASHBOARD: Stack trace: " + ex.StackTrace);
                
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsStarted = false;
                    UpdateButtonState();
                });
            }
            finally
            {
                timeoutCts?.Dispose();
                IsSessionStarting = false;
                UpdateButtonState();
                _loggingService.LogInformation("DASHBOARD: Startup process completed (IsSessionStarting = false)");
            }
        }

        private async Task StopWorkflowAsync()
        {
            try
            {
                _loggingService.LogInformation("USER ACTION: Stop button clicked - stopping workflow");

                var result = await _scanWorkflowService
                    .StopAsync(_cancellationTokenSource.Token)
                    .ConfigureAwait(false);

                if (result.Success)
                {
                    IsStarted = false;
                    ClearScanData();
                    _loggingService.LogInformation("DASHBOARD: Scan workflow stopped successfully");
                }
                else
                {
                    _loggingService.LogWarning("DASHBOARD: Workflow stop completed with warnings: " + result.Message);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("DASHBOARD: Error stopping scan workflow: " + ex.Message);
            }
        }

        private void OnWorkflowStatusChanged(object? sender, WorkflowStatusChangedEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                _loggingService.LogInformation($"WORKFLOW STATUS CHANGED: {e.PreviousStatus} → {e.CurrentStatus}");
                
                switch (e.CurrentStatus)
                {
                    case WorkflowStatus.Starting:
                        IsSessionStarting = true;
                        UpdateButtonState();
                        _loggingService.LogInformation("UI: Status - Starting services...");
                        break;
                    case WorkflowStatus.Running:
                        IsStarted = true;
                        IsSessionStarting = false;
                        UpdateButtonState();
                        _loggingService.LogInformation("UI: Status - System Ready, Workflow operational");
                        break;
                    case WorkflowStatus.Stopped:
                        IsStarted = false;
                        IsSessionStarting = false;
                        UpdateButtonState();
                        _loggingService.LogInformation("UI: Status - Workflow stopped");
                        break;
                    case WorkflowStatus.Error:
                        IsStarted = false;
                        IsSessionStarting = false;
                        _loggingService.LogError("WORKFLOW ERROR STATUS: " + e.Message);
                        UpdateButtonState();
                        _loggingService.LogInformation("UI: Status - Error occurred, system reset, ready for retry");
                        break;
                }
            });
        }

        private void OnScanDataReceived(object? sender, ScanDataReceivedEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                PID = e.PID;
                WorkOrder = e.WorkOrder;
                PartNo = e.PartNumber;
                Result = e.Result;
                ResultMessage = e.Message;

                _loggingService.LogInformation($"Scan data received: PID={e.PID}, WO={e.WorkOrder}, PN={e.PartNumber}, Result={e.Result}");

                // Play appropriate sound
                if (e.Result == "OK")
                {
                    _ = Task.Run(() => PlayBeepSound());                    
                }
                else
                {
                    _ = Task.Run(() => PlayNgSound());
                }
            });
        }

        private void OnWorkflowErrorOccurred(object? sender, ErrorOccurredEventArgs e)
        {
            _loggingService.LogError("Workflow error: " + e.ErrorMessage);
            if (e.Exception != null)
            {
                _loggingService.LogError("Exception details: " + e.Exception.ToString());
            }
            
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                // Only handle critical errors that require stopping the system
                if (e.Severity == ErrorSeverity.Critical)
                {
                    IsStarted = false;
                    _loggingService.LogError("CRITICAL ERROR: System stopped due to critical error");
                    _ = Task.Run(() => PlayNgSound());
                }
            });
        }

        private void OnNewLogEntry(object? sender, LogEntry logEntry)
        {
            const int MaxLogLines = 100;
            
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var newLogLine = $"{logEntry.Timestamp:HH:mm:ss.fff}\t[{logEntry.Level}]\t{logEntry.Message}";
                var currentLines = Logs.Split('\n').ToList();
                
                currentLines.Add(newLogLine);
                
                if (currentLines.Count > MaxLogLines)
                {
                    currentLines.RemoveRange(0, currentLines.Count - MaxLogLines);
                }
                
                Logs = string.Join('\n', currentLines);
            });
        }

        private void ClearScanData()
        {
            PID = string.Empty;
            WorkOrder = string.Empty;
            PartNo = string.Empty;
            Result = string.Empty;
            ResultMessage = string.Empty;
            PCBLocation = string.Empty;
            InformationMessage = string.Empty;
            IsMessageOn = false;
        }

        #region Audio Methods
        private void PlayNgSound()
        {
            try
            {
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "scan_fail.wav");
                using var player = new SoundPlayer(filePath);
                player.Play();
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Error playing NG sound: " + ex.Message);
            }
        }

        private void PlayBeepSound()
        {
            try
            {
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "ScannerBeepSound.wav");
                using var player = new SoundPlayer(filePath);
                player.Play();
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Error playing beep sound: " + ex.Message);
            }
        }
        #endregion

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                _loggingService.LogInformation("DASHBOARD: Starting disposal process...");
                
                // Unsubscribe from events
                _scanWorkflowService.StatusChanged -= OnWorkflowStatusChanged;
                _scanWorkflowService.ScanDataReceived -= OnScanDataReceived;
                _scanWorkflowService.ErrorOccurred -= OnWorkflowErrorOccurred;
                _loggingService.OnNewLog -= OnNewLogEntry;
                
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                
                _scanWorkflowService.Dispose();
                
                _loggingService.LogInformation("DASHBOARD: DashboardViewModel disposed successfully");
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning("DASHBOARD: Error during ViewModel disposal: " + ex.Message);
            }

            _disposed = true;
        }
    }
}
