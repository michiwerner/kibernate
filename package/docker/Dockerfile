FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["kibernate.csproj", "./"]
RUN dotnet restore "kibernate.csproj"
COPY . .
WORKDIR "/src/"
RUN dotnet build "kibernate.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "kibernate.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
RUN mkdir /etc/kibernate
COPY --from=publish /src/configs/testing.yml /etc/kibernate/kibernate.yml
ENTRYPOINT ["dotnet", "kibernate.dll"]
