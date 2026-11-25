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

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [Trait("Story", "US-09")]
    public void NewReplacesExisting_TorSessionOverANearbyOne_AlwaysKeepsTheTorSession(bool existingOpenedHere, bool newOpenedHere, bool ownStaticKeyIsLower)
    {
        Assert.True(SessionPreference.NewReplacesExisting(ConnectionRoute.Nearby, existingOpenedHere, Young, ConnectionRoute.Tor, newOpenedHere, ownStaticKeyIsLower, Window));
        Assert.True(SessionPreference.NewReplacesExisting(ConnectionRoute.Nearby, existingOpenedHere, Old, ConnectionRoute.Tor, newOpenedHere, ownStaticKeyIsLower, Window));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [Trait("Story", "US-09")]
    public void NewReplacesExisting_NearbySessionOverAYoungTorOne_KeepsTheTorSession(bool existingOpenedHere, bool newOpenedHere, bool ownStaticKeyIsLower)
    {
        Assert.False(SessionPreference.NewReplacesExisting(ConnectionRoute.Tor, existingOpenedHere, Young, ConnectionRoute.Nearby, newOpenedHere, ownStaticKeyIsLower, Window));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [Trait("Story", "US-09")]
    public void NewReplacesExisting_NearbySessionOverATorOneOlderThanTheWindow_KeepsTheNearbySession(bool existingOpenedHere, bool newOpenedHere, bool ownStaticKeyIsLower)
    {
        Assert.True(SessionPreference.NewReplacesExisting(ConnectionRoute.Tor, existingOpenedHere, Old, ConnectionRoute.Nearby, newOpenedHere, ownStaticKeyIsLower, Window));
    }

    [Theory]
    [InlineData(ConnectionRoute.Nearby, true, false)]
    [InlineData(ConnectionRoute.Nearby, false, true)]
    [InlineData(ConnectionRoute.Tor, true, true)]
    [InlineData(ConnectionRoute.Tor, false, false)]
    public void NewReplacesExisting_SameRoute_FollowsTheRuleWithoutRoutes(ConnectionRoute route, bool existingOpenedHere, bool ownStaticKeyIsLower)
    {
        var expected = SessionPreference.NewReplacesExisting(existingOpenedHere, Young, !existingOpenedHere, ownStaticKeyIsLower, Window);

        Assert.Equal(expected, SessionPreference.NewReplacesExisting(route, existingOpenedHere, Young, route, !existingOpenedHere, ownStaticKeyIsLower, Window));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [Trait("Story", "US-09")]
    public void NewReplacesExisting_TorAndNearbyOpenedAtOnce_BothSidesKeepTheTorSession(bool aliceIsLower, bool torOpenedByAlice)
    {
        var keptWhereTorCameFirst = KeptAcrossRoutes(existingRoute: ConnectionRoute.Tor, existingOpenedHere: torOpenedByAlice, newOpenedHere: !torOpenedByAlice, ownStaticKeyIsLower: aliceIsLower);
        var keptWhereNearbyCameFirst = KeptAcrossRoutes(existingRoute: ConnectionRoute.Nearby, existingOpenedHere: torOpenedByAlice, newOpenedHere: !torOpenedByAlice, ownStaticKeyIsLower: !aliceIsLower);

        Assert.Equal(ConnectionRoute.Tor, keptWhereTorCameFirst);
        Assert.Equal(ConnectionRoute.Tor, keptWhereNearbyCameFirst);
    }

    private static ConnectionRoute KeptAcrossRoutes(ConnectionRoute existingRoute, bool existingOpenedHere, bool newOpenedHere, bool ownStaticKeyIsLower)
    {
        var newRoute = existingRoute == ConnectionRoute.Tor ? ConnectionRoute.Nearby : ConnectionRoute.Tor;
        var replaces = SessionPreference.NewReplacesExisting(existingRoute, existingOpenedHere, Young, newRoute, newOpenedHere, ownStaticKeyIsLower, Window);
        return replaces ? newRoute : existingRoute;
    }
}
