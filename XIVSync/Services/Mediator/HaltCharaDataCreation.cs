namespace XIVSync.Services.Mediator;

public record HaltCharaDataCreation(bool Resume = false) : SameThreadMessage();
