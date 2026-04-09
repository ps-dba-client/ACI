#requires -Version 5.1
<#
  Minimal-cost lab deploy (Windows): creates Azure resources, builds images in ACR (cloud build),
  then applies the Container App.

  Prerequisites:
  - Azure CLI:  az login
  - Terraform:  https://developer.hashicorp.com/terraform/install
  - Copy terraform\terraform.tfvars.example to terraform\terraform.tfvars and set secrets there,
    OR set TF_VAR_splunk_hec_token and other TF_VAR_* outside this script.

  Usage (from the dotnet lab folder — the directory that contains Dockerfile and otel-collector\):
    .\scripts\deploy.ps1
    .\scripts\deploy.ps1 -ImageTag v2
#>
param(
    [string]$ImageTag = "v1"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $Root

Write-Host "=== Step 1/3: Terraform apply (resource group, Log Analytics, ACA env, ACR) ===" -ForegroundColor Cyan
Push-Location "$Root\terraform"
try {
    terraform init
    terraform apply -auto-approve `
        -target=azurerm_resource_group.main `
        -target=azurerm_log_analytics_workspace.main `
        -target=azurerm_container_app_environment.main `
        -target=azurerm_container_registry.acr
}
finally {
    Pop-Location
}

$Rg = terraform -chdir="$Root\terraform" output -raw resource_group_name
$Acr = terraform -chdir="$Root\terraform" output -raw acr_name

Write-Host "=== Step 2/3: ACR cloud build (dotnet app + Splunk OTel collector sidecar image) ===" -ForegroundColor Cyan
az acr build --resource-group $Rg --registry $Acr --file Dockerfile --image "aca-otel-dotnet:$ImageTag" $Root
if ($LASTEXITCODE -ne 0) { throw "acr build (dotnet) failed" }

az acr build --resource-group $Rg --registry $Acr --file Dockerfile --image "aca-otel-collector:$ImageTag" "$Root\otel-collector"
if ($LASTEXITCODE -ne 0) { throw "acr build (collector) failed" }

Write-Host "=== Step 3/3: Terraform apply (Container App) ===" -ForegroundColor Cyan
Push-Location "$Root\terraform"
try {
    terraform apply -auto-approve -var "image_tag=$ImageTag"
}
finally {
    Pop-Location
}

Write-Host "=== Outputs ===" -ForegroundColor Green
terraform -chdir="$Root\terraform" output
