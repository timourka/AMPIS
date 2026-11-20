FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
ARG SERVICE
COPY . .
RUN dotnet publish "$SERVICE/$SERVICE.csproj" -c Release -o /out
FROM mcr.microsoft.com/dotnet/aspnet:9.0
ARG SERVICE
ENV SERVICE=$SERVICE
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
CMD exec dotnet "$SERVICE.dll"
