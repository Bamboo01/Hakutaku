# build the VueJS admin UI -> static files
FROM node:22 AS web
WORKDIR /web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN npm run build
 
# 2. build and publish the ASP.NET app
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY . .
RUN dotnet publish server/Hakutaku.Server.csproj -c Release -o /out
 
# 3. runtime image: only the published output and the built UI
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /out .
COPY --from=web /web/dist ./wwwroot
EXPOSE 8080
ENTRYPOINT ["dotnet", "Hakutaku.Server.dll"]
