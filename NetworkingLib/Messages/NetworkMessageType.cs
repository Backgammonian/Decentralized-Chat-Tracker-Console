﻿namespace NetworkingLib.Messages
{
    public enum NetworkMessageType : byte
    {
        Empty = 0,
        ExampleMessage = 10,

        IntroduceClientToTracker,
        IntroduceClientToTrackerResponse,
        IntroduceClientToTrackerError,
        CommandToTracker,
        CommandReceiptNotification,
        CommandToTrackerError,
        UserConnectionResponse,
        ForwardedConnectionRequest,
        ListOfUsersWithDesiredNickname,
        UserNotFoundError,
        PingResponse,
        TimeResponse,
        UpdatedInfoForTracker,
    }
}