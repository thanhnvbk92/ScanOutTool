using System;
using System.Threading;
using System.Threading.Tasks;

namespace ScanOutTool.Services.Orchestration
{
    /// <summary>
    /// Service qu?n lý toàn b? workflow scanning - Business Logic Layer
    /// </summary>
    public interface IScanWorkflowService : IDisposable
    {
        event EventHandler<WorkflowStatusChangedEventArgs> StatusChanged;
        event EventHandler<ScanDataReceivedEventArgs> ScanDataReceived;
        event EventHandler<ErrorOccurredEventArgs> ErrorOccurred;

        bool IsRunning { get; }
        WorkflowStatus CurrentStatus { get; }

        Task<WorkflowResult> StartAsync(CancellationToken cancellationToken = default);
        Task<WorkflowResult> StopAsync(CancellationToken cancellationToken = default);
    }

    public class WorkflowStatusChangedEventArgs : EventArgs
    {
        public WorkflowStatus PreviousStatus { get; init; }
        public WorkflowStatus CurrentStatus { get; init; }
        public string? Message { get; init; }
    }

    public class ScanDataReceivedEventArgs : EventArgs
    {
        public string PID { get; init; } = string.Empty;
        public string WorkOrder { get; init; } = string.Empty;
        public string PartNumber { get; init; } = string.Empty;
        public string Result { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; } = DateTime.Now;
    }

    public class ErrorOccurredEventArgs : EventArgs
    {
        public string ErrorMessage { get; init; } = string.Empty;
        public Exception? Exception { get; init; }
        public ErrorSeverity Severity { get; init; }
    }

    public enum WorkflowStatus
    {
        Stopped,
        Starting,
        Running,
        Stopping,
        Error
    }

    public enum ErrorSeverity
    {
        Warning,
        Error,
        Critical
    }

    public class WorkflowResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public Exception? Exception { get; init; }

        public static WorkflowResult CreateSuccess(string message = "") 
            => new() { Success = true, Message = message };

        public static WorkflowResult CreateFailure(string message, Exception? exception = null)
            => new() { Success = false, Message = message, Exception = exception };
    }
}