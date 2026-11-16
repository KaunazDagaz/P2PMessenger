namespace P2PMessenger.Transport.Admission;

internal enum AdmissionRefusal
{
    None = 0,
    TooManyPending = 1,
    TooManyPendingFromSource = 2,
    TooManyHandshakes = 3,
    TooManyHandshakesFromSource = 4,
    TooManySources = 5,
}
