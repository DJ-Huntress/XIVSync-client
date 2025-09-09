using System;
using XIVSync.MareConfiguration.Models;

namespace XIVSync.Services.Mediator;

public record NotificationMessage(string Title, string Message, NotificationType Type, TimeSpan? TimeShownOnScreen = null) : MessageBase();
