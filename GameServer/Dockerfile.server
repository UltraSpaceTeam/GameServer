FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["GameServer/GameServer.csproj", "GameServer/"]
RUN dotnet restore "GameServer/GameServer.csproj"

COPY . .
WORKDIR "/src/GameServer"

RUN dotnet publish "GameServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

RUN dotnet tool install --global dotnet-ef --version 8.*
ENV PATH="$PATH:/root/.dotnet/tools"

RUN dotnet ef migrations bundle --self-contained -r linux-x64 -o /app/publish/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .

RUN echo '#!/bin/sh\n\
echo "Attempting to apply EF Migrations..."\n\
./efbundle --connection "$ConnectionStrings__DefaultConnection" || true\n\
echo "Starting GameServer..."\n\
dotnet GameServer.dll' > entrypoint.sh

RUN chmod +x entrypoint.sh efbundle

ENTRYPOINT ["./entrypoint.sh"]
