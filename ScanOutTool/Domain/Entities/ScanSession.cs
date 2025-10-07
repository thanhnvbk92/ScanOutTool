using System;

namespace ScanOutTool.Domain.Entities
{
    /// <summary>
    /// Core business entity representing a scanning session
    /// </summary>
    public class ScanSession
    {
        public Guid Id { get; private set; }
        public string PID { get; private set; }
        public string WorkOrder { get; private set; }
        public string PartNumber { get; private set; }
        public ScanResult Result { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public DateTime? CompletedAt { get; private set; }
        public ScanSessionState State { get; private set; }
        public string? ErrorMessage { get; private set; }

        private ScanSession() { } // EF Constructor

        public ScanSession(string pid, string workOrder, string partNumber)
        {
            if (string.IsNullOrWhiteSpace(pid))
                throw new ArgumentException("PID cannot be null or empty", nameof(pid));
            if (string.IsNullOrWhiteSpace(workOrder))
                throw new ArgumentException("Work order cannot be null or empty", nameof(workOrder));
            if (string.IsNullOrWhiteSpace(partNumber))
                throw new ArgumentException("Part number cannot be null or empty", nameof(partNumber));

            Id = Guid.NewGuid();
            PID = pid.Trim();
            WorkOrder = workOrder.Trim();
            PartNumber = partNumber.Trim();
            State = ScanSessionState.Created;
            CreatedAt = DateTime.UtcNow;
        }

        public void StartProcessing()
        {
            if (State != ScanSessionState.Created)
                throw new InvalidOperationException($"Cannot start processing from state {State}");
            
            State = ScanSessionState.Processing;
        }

        public void Complete(ScanResult result, string? message = null)
        {
            if (State != ScanSessionState.Processing)
                throw new InvalidOperationException($"Cannot complete from state {State}");

            Result = result;
            State = ScanSessionState.Completed;
            CompletedAt = DateTime.UtcNow;
            
            if (result == ScanResult.Failed && !string.IsNullOrEmpty(message))
                ErrorMessage = message;
        }

        public void Fail(string errorMessage)
        {
            if (State == ScanSessionState.Completed)
                throw new InvalidOperationException("Cannot fail a completed session");

            State = ScanSessionState.Failed;
            Result = ScanResult.Failed;
            ErrorMessage = errorMessage;
            CompletedAt = DateTime.UtcNow;
        }
    }

    public enum ScanSessionState
    {
        Created,
        Processing,
        Completed,
        Failed
    }

    public enum ScanResult
    {
        Unknown,
        Passed,
        Failed
    }
}