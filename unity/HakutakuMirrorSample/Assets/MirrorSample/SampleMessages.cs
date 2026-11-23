using Mirror;

namespace HakutakuMirrorSample
{
    public struct SampleAuthRequest : NetworkMessage
    {
        // The player's Hakutaku session ticket. Empty if the client couldn't log in. A credential: never log it.
        public string ticket;
    }

    public struct SampleAuthResponse : NetworkMessage
    {
        public bool ok;
        // One of SampleAuthRejectReason when ok is false
        public byte reason;
    }

    public static class SampleAuthRejectReason
    {
        public const byte InvalidTicket = 1;
    }

    // What the server tells a client about itself, for the test screen
    public struct SampleStatusMessage : NetworkMessage
    {
        public string playerId;
        public string displayName;
        public long xp;
        public bool online;
    }
}
