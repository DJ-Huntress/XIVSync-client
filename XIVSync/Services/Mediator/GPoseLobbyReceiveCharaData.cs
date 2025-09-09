using XIVSync.API.Dto.CharaData;

namespace XIVSync.Services.Mediator;

public record GPoseLobbyReceiveCharaData(CharaDataDownloadDto CharaDataDownloadDto) : MessageBase();
