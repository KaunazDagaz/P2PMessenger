using P2PMessenger.Core;

namespace P2PMessenger.Application.Errors;

public sealed record FailureReport(FailureSource Source, string ExceptionType);
