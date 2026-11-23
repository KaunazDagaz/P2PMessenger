using Android.Content;

namespace P2PMessenger.Android.Scanning;

internal readonly record struct ActivityOutcome(Result Result, Intent? Data);
