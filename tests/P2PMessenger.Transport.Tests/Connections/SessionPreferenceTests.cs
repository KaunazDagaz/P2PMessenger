using P2PMessenger.Transport.Connections;

namespace P2PMessenger.Transport.Tests.Connections;

public sealed class SessionPreferenceTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Young = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Old = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void NewReplacesExisting_BothOpenedBySameSide_KeepsTheNewOne(bool openedHere, bool ownStaticKeyIsLower)
    {
        Assert.True(SessionPreference.NewReplacesExisting(openedHere, Young, openedHere, ownStaticKeyIsLower, Window));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void NewReplacesExisting_ExistingOlderThanTheWindow_KeepsTheNewOne(bool existingOpenedHere, bool ownStaticKeyIsLower)
    {
        Assert.True(SessionPreference.NewReplacesExisting(existingOpenedHere, Old, !existingOpenedHere, ownStaticKeyIsLower, Window));
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void NewReplacesExisting_OpenedBySidesAtOnce_KeepsTheSessionOpenedByTheLowerKey(bool existingOpenedHere, bool ownStaticKeyIsLower, bool expected)
    {
        Assert.Equal(expected, SessionPreference.NewReplacesExisting(existingOpenedHere, Young, !existingOpenedHere, ownStaticKeyIsLower, Window));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, false, false)]
    public void NewReplacesExisting_SimultaneousOpen_BothSidesKeepTheSameSession(bool aliceIsLower, bool aliceSawHerOwnFirst, bool bobSawHisOwnFirst)
    {
        var keptByAlice = Kept(ownStaticKeyIsLower: aliceIsLower, ownSession: "alice", peerSession: "bob", ownFirst: aliceSawHerOwnFirst);
        var keptByBob = Kept(ownStaticKeyIsLower: !aliceIsLower, ownSession: "bob", peerSession: "alice", ownFirst: bobSawHisOwnFirst);

        Assert.Equal(keptByAlice, keptByBob);
        Assert.Equal(aliceIsLower ? "alice" : "bob", keptByAlice);
    }

    private static string Kept(bool ownStaticKeyIsLower, string ownSession, string peerSession, bool ownFirst)
    {
        var existing = ownFirst ? ownSession : peerSession;
        var candidate = ownFirst ? peerSession : ownSession;
        var replaces = SessionPreference.NewReplacesExisting(
            existingOpenedHere: existing == ownSession,
            existingAge: Young,
            newOpenedHere: candidate == ownSession,
            ownStaticKeyIsLower: ownStaticKeyIsLower,
            simultaneousOpenWindow: Window);
        return replaces ? candidate : existing;
    }
}
