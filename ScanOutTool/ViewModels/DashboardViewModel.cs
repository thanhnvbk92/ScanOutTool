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
        private readonly IConfigService _configService; // ✅ RESTORED: Config service dependency
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
        [ObservableProperty] private string _selectedEBR = string.Empty; // ✅ RESTORED: Selected EBR property
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
            ILoggingService loggingService,
            IConfigService configService)
        {
            _scanWorkflowService = scanWorkflowService ?? throw new ArgumentNullException(nameof(scanWorkflowService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));

            InitializeViewModel();
            SubscribeToEvents();
        }

        private void InitializeViewModel()
        {
            RunModes = Enum.GetValues<RunMode>().ToList();
            
            // Load from config
            SelectedRunMode = (RunMode)(int)_configService.Config.SelectedRunMode;
            SelectedEBR = _configService.Config.SelectedEBR;
            
            IsStarted = false;
            StartBtnText = "START";
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
                _loggingService.LogInformation("Starting workflow...");

                // Add timeout to prevent hanging
                timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _cancellationTokenSource.Token, timeoutCts.Token);
                
                var result = await _scanWorkflowService
                    .StartAsync(linkedCts.Token)
                    .ConfigureAwait(false);

                if (result.Success)
                {
                    IsStarted = true;
                    _loggingService.LogInformation("Workflow started successfully - System ready");
                    
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        UpdateButtonState();
                    });
                }
                else
                {
                    _loggingService.LogError("Workflow startup failed: " + result.Message);
                    if (result.Exception != null)
                    {
                        _loggingService.LogError("Exception: " + result.Exception.Message);
                    }
                    
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        IsStarted = false;
                        UpdateButtonState();
                    });
                }
            }
            catch (OperationCanceledException) when (timeoutCts?.Token.IsCancellationRequested == true)
            {
                _loggingService.LogError("Workflow startup timeout after 30 seconds");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsStarted = false;
                    UpdateButtonState();
                });
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Workflow startup error: " + ex.Message);
                
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
            }
        }

        private async Task StopWorkflowAsync()
        {
            try
            {
                _loggingService.LogInformation("Stopping workflow...");

                var result = await _scanWorkflowService
                    .StopAsync(_cancellationTokenSource.Token)
                    .ConfigureAwait(false);

                if (result.Success)
                {
                    IsStarted = false;
                    ClearScanData();
                    _loggingService.LogInformation("Workflow stopped successfully");
                }
                else
                {
                    _loggingService.LogWarning("Workflow stop completed with warnings: " + result.Message);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Error stopping workflow: " + ex.Message);
            }
        }

        private void OnWorkflowStatusChanged(object? sender, WorkflowStatusChangedEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                switch (e.CurrentStatus)
                {
                    case WorkflowStatus.Starting:
                        IsSessionStarting = true;
                        UpdateButtonState();
                        break;
                    case WorkflowStatus.Running:
                        IsStarted = true;
                        IsSessionStarting = false;
                        UpdateButtonState();
                        break;
                    case WorkflowStatus.Stopped:
                        IsStarted = false;
                        IsSessionStarting = false;
                        UpdateButtonState();
                        break;
                    case WorkflowStatus.Error:
                        IsStarted = false;
                        IsSessionStarting = false;
                        _loggingService.LogError("Workflow error: " + e.Message);
                        UpdateButtonState();
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

                // Update magazine quantity from HMES if available
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var hmesService = _scanWorkflowService as ScanWorkflowService;
                        if (hmesService != null)
                        {
                            // Get the HMES service via reflection to access GetMagazineQtyAsync
                            var hmesField = hmesService.GetType().GetField("_hmesService", 
                                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            
                            if (hmesField?.GetValue(hmesService) is HMESService actualHmesService)
                            {
                                var qty = await actualHmesService.GetMagazineQtyAsync();
                                if (qty >= 0)
                                {
                                    await Application.Current.Dispatcher.InvokeAsync(() =>
                                    {
                                        MagazineQty = qty;
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogError("Error updating magazine quantity: " + ex.Message);
                    }
                });

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
            
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                // Only handle critical errors that require stopping the system
                if (e.Severity == ErrorSeverity.Critical)
                {
                    IsStarted = false;
                    _loggingService.LogError("CRITICAL ERROR: System stopped");
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

        // Property change handlers to sync with config
        partial void OnSelectedRunModeChanged(RunMode value)
        {
            _configService.Config.SelectedRunMode = (AppConfig.RunMode)(int)value;
        }

        partial void OnSelectedEBRChanged(string value)
        {
            _configService.Config.SelectedEBR = value;
        }

        partial void OnIsSessionStartingChanged(bool value)
        {
            UpdateButtonState();
        }

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // Unsubscribe from events
                _scanWorkflowService.StatusChanged -= OnWorkflowStatusChanged;
                _scanWorkflowService.ScanDataReceived -= OnScanDataReceived;
                _scanWorkflowService.ErrorOccurred -= OnWorkflowErrorOccurred;
                _loggingService.OnNewLog -= OnNewLogEntry;
                
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                
                _scanWorkflowService.Dispose();
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning("Error during ViewModel disposal: " + ex.Message);
            }

            _disposed = true;
        }
    }
}
