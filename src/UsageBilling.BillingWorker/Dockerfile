FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/UsageBilling.BillingWorker/UsageBilling.BillingWorker.csproj
RUN dotnet publish src/UsageBilling.BillingWorker/UsageBilling.BillingWorker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "UsageBilling.BillingWorker.dll"]