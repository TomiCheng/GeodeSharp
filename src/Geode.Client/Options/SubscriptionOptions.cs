namespace Geode.Client.Options;

/// <summary>
/// Subscription, durable-client, and event-notification settings.
/// Mirrors the subscription-related fields of cppcache
/// <c>SystemProperties</c>. The whole group is dormant until Phase 12+
/// adds CQ / register-interest / event listeners.
/// </summary>
public class SubscriptionOptions
{
    /// <summary>
    /// Stable client identifier that lets the server retain this client's
    /// subscription queue across reconnects. Mirrors cppcache
    /// <c>durable-client-id</c>; default empty (= non-durable, server
    /// discards the queue on disconnect).
    /// </summary>
    /// <remarks>
    /// Set a stable string (e.g. <c>"order-service-pod-1"</c>) to opt into
    /// durable subscriptions. Consumed by the
    /// <c>ClientProxyMembershipID</c> builder when subscriptions ship in
    /// Phase 12+.
    /// </remarks>
    public string DurableClientId { get; set; } = string.Empty;

    /// <summary>
    /// How long the server should retain this client's subscription queue
    /// after a disconnect before giving up. Mirrors cppcache
    /// <c>durable-timeout</c>; default 300 seconds. Only meaningful when
    /// <see cref="DurableClientId"/> is set.
    /// </summary>
    public TimeSpan DurableTimeout { get; set; } = TimeSpan.FromSeconds(300);

    /// <summary>
    /// Whether a non-durable client starts receiving subscription events
    /// automatically once regions are created. Mirrors cppcache
    /// <c>auto-ready-for-events</c>; default <c>true</c>. Set to
    /// <c>false</c> to require an explicit "ready" call after wiring up
    /// listeners (Phase 12+ API).
    /// </summary>
    public bool AutoReadyForEvents { get; set; } = true;

    /// <summary>
    /// How often the client checks subscription redundancy (HA queue copy
    /// count). Mirrors cppcache <c>redundancy-monitor-interval</c>;
    /// default 10 seconds.
    /// </summary>
    public TimeSpan RedundancyMonitorInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Periodic ack cadence for received subscription notifications.
    /// Mirrors cppcache <c>notify-ack-interval</c>; default 1 second.
    /// </summary>
    public TimeSpan NotifyAckInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long an idle event-id map entry is kept for duplicate-event
    /// detection on the subscription channel. Mirrors cppcache
    /// <c>notify-dupcheck-life</c>; default 300 seconds.
    /// </summary>
    public TimeSpan NotifyDupCheckLife { get; set; } = TimeSpan.FromSeconds(300);

    /// <summary>
    /// Per-client event-conflation override sent in the handshake's
    /// "overrides" byte. Mirrors cppcache <c>conflate-events</c>:
    /// <list type="bullet">
    ///   <item><c>"server"</c> (default) — defer to server-side setting.</item>
    ///   <item><c>"true"</c>  — force conflation on for this client.</item>
    ///   <item><c>"false"</c> — force conflation off for this client.</item>
    /// </list>
    /// MVP hard-codes the override byte to 0 (= "server") in
    /// <see cref="Geode.Client.Protocol.TcrConnection"/>; this property
    /// will be wired in once the handshake reads it.
    /// </summary>
    public string ConflateEvents { get; set; } = "server";
}
