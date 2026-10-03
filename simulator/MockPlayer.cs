using Hakutaku;

namespace simulator
{
    // One pretend player: their own SDK client, so their own session, plus what a real
    // install would keep -- the device token from RegisterGuest, and the email and password
    // once linked.
    class MockPlayer
    {
        public string Name { get; }
        public HakutakuClientInstanceAPI Client { get; }
        // The pretend phone's hardware ID. The server only records it.
        public string HardwareId { get; init; } = NewHardwareId();
        // The guest's credential. Null for a slot that signed in by email, and once
        // verifying an email has removed it.
        public string? DeviceToken { get; set; }
        public string? Email { get; set; }
        public string? Password { get; set; }
        // As the server last reported it.
        public string? DisplayName { get; set; }

        public MockPlayer(string name, string serverUrl)
        {
            Name = name;
            Client = new HakutakuClientInstanceAPI(new HakutakuApiSettings { ServerUrl = serverUrl });
        }

        public string? PlayerId => Client.authenticationContext.PlayerId;

        public static string NewHardwareId() => $"sim-hw-{Guid.NewGuid():N}";

        // example.com is reserved and accepts no mail (RFC 2606), so a server with SMTP
        // configured doesn't send these anywhere.
        public static string NewEmail() => $"sim-{Guid.NewGuid():N}"[..16] + "@example.com";

        public static string NewPassword() => $"sim-{Guid.NewGuid():N}";
    }
}
