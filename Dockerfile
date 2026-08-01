FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Copy the project file first so package restore can be cached
COPY CCTavern/CCTavern.csproj CCTavern/
RUN dotnet restore CCTavern/CCTavern.csproj

# Copy the rest of the source
COPY . .

# Publish from the project file
RUN dotnet publish CCTavern/CCTavern.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble AS runtime
WORKDIR /app

COPY --from=build /app/publish/ ./

ENTRYPOINT ["dotnet", "CCTavern.dll"]