# Generate C# Client from OpenAPI Schema
# Generates a typed C# client from the OpenAPI schema using NSwag or openapi-generator

param(
    [string]$Tool = "nswag",  # "nswag" or "openapi-generator"
    [string]$OutputPath = "src\VoiceStudio.Core\Services\Generated\BackendClient.generated.cs",
    [string]$Namespace = "VoiceStudio.Core.Services.Generated"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$openapiFile = Join-Path $projectRoot "docs\api\openapi.json"
$outputDir = Split-Path -Parent $OutputPath

# Ensure output directory exists
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# Check if OpenAPI file exists
if (-not (Test-Path $openapiFile)) {
    Write-Error "OpenAPI schema file not found: $openapiFile"
    Write-Host "Run: python scripts\export_openapi_schema.py" -ForegroundColor Yellow
    exit 1
}

Write-Host "Generating C# client from OpenAPI schema..." -ForegroundColor Cyan
Write-Host "  OpenAPI file: $openapiFile" -ForegroundColor Gray
Write-Host "  Output: $OutputPath" -ForegroundColor Gray
Write-Host "  Tool: $Tool" -ForegroundColor Gray

if ($Tool -eq "nswag") {
    # Check if NSwag is installed
    $nswagInstalled = $false
    try {
        $nswagVersion = nswag version 2>&1
        if ($LASTEXITCODE -eq 0) {
            $nswagInstalled = $true
            Write-Host "  Found NSwag: $nswagVersion" -ForegroundColor Green
        }
    }
    catch {
        # NSwag not found
    }

    if (-not $nswagInstalled) {
        Write-Host "`n[WARN] NSwag not found. Installing..." -ForegroundColor Yellow
        Write-Host "  Installing NSwag.ConsoleCore..." -ForegroundColor Gray
        
        try {
            dotnet tool install -g NSwag.ConsoleCore
            if ($LASTEXITCODE -eq 0) {
                Write-Host "  [OK] NSwag installed successfully" -ForegroundColor Green
            }
            else {
                throw "Failed to install NSwag"
            }
        }
        catch {
            Write-Error "Failed to install NSwag. Please install manually: dotnet tool install -g NSwag.ConsoleCore"
            exit 1
        }
    }

    # Generate client using NSwag
    Write-Host "`nGenerating C# client with NSwag..." -ForegroundColor Cyan
    
    $nswagConfig = @"
{
  `"runtime`": `"Net80`",
  `"defaultVariables`": null,
  `"documentGenerator`": {
    `"fromDocument`": {
      `"json`": `"$($openapiFile.Replace('\', '\\'))`",
      `"url`": null,
      `"output`": null,
      `"newLineBehavior`": `"Auto`"
    }
  },
  `"codeGenerators`": {
    `"openApiToCSharpClient`": {
      `"operationGenerationMode`": `"SingleClientFromOperationId`",
      `"clientBaseClass`": null,
      `"clientClassAccessModifier`": `"public`",
      `"useBaseUrl`": true,
      `"generateBaseUrlProperty`": true,
      `"generateSyncMethods`": false,
      `"generatePrepareRequestAndProcessResponseAsAsyncMethods`": false,
      `"injectHttpClient`": true,
      `"disposeHttpClient`": true,
      `"protectedMethods`": [],
      `"useHttpClientCreationMethod`": false,
      `"useHttpRequestMessageCreationMethod`": false,
      `"useTransformOptionsMethod`": false,
      `"useTransformResultMethod`": false,
      `"generateDtoTypes`": true,
      `"generateOptionalParameters`": false,
      `"generateJsonMethods`": false,
      `"requiredPropertiesMustBeDefined`": true,
      `"dateType`": `"System.DateTimeOffset`",
      `"jsonConverters`": null,
      `"anyType`": `"object`",
      `"dateTimeType`": `"System.DateTimeOffset`",
      `"timeType`": `"System.TimeSpan`",
      `"timeSpanType`": `"System.TimeSpan`",
      `"arrayType`": `"System.Collections.Generic.ICollection`",
      `"arrayInstanceType`": `"System.Collections.ObjectModel.Collection`",
      `"dictionaryType`": `"System.Collections.Generic.IDictionary`",
      `"dictionaryInstanceType`": `"System.Collections.Generic.Dictionary`",
      `"arrayBaseType`": `"System.Collections.ObjectModel.Collection`",
      `"dictionaryBaseType`": `"System.Collections.Generic.Dictionary`",
      `"classStyle`": `"Poco`",
      `"jsonLibrary`": `"SystemTextJson`",
      `"generateDefaultValues`": true,
      `"generateDataAnnotations`": true,
      `"excludedTypeNames`": [],
      `"excludedParameterNames`": [],
      `"handleReferences`": false,
      `"generateImmutableArrayProperties`": false,
      `"generateImmutableDictionaryProperties`": false,
      `"jsonSerializerSettingsTransformationMethod`": null,
      `"inlineNamedArrays`": false,
      `"inlineNamedDictionaries`": false,
      `"inlineNamedTuples`": true,
      `"inlineNamedAny`": false,
      `"generateDtoTypes`": true,
      `"generateOptionalPropertiesAsNullable`": false,
      `"generateNullableReferenceTypes`": false,
      `"templateDirectory`": null,
      `"typeNameGeneratorType`": null,
      `"propertyNameGeneratorType`": null,
      `"enumNameGeneratorType`": null,
      `"serviceHost`": null,
      `"serviceSchemes`": null,
      `"output`": `"$($OutputPath.Replace('\', '\\'))`",
      `"newLineBehavior`": `"Auto`",
      `"namespace`": `"$Namespace`",
      `"requiredPropertiesMustBeDefined`": true,
      `"generateExceptionClasses`": true,
      `"exceptionClass`": `"ApiException`",
      `"wrapDtoExceptions`": true,
      `"useHttpClientCreationMethod`": false,
      `"httpClientType`": `"System.Net.Http.HttpClient`",
      `"useHttpRequestMessageCreationMethod`": false,
      `"useBaseUrl`": true,
      `"baseUrlTokenName`": `"BASE_URL`",
      `"queryNullValue`": `""`,
      `"useTransformOptionsMethod`": false,
      `"useTransformResultMethod`": false,
      `"generateDtoTypes`": true,
      `"generateOptionalParameters`": false,
      `"generateJsonMethods`": false,
      `"parameterDateTimeFormat`": null,
      `"parameterDateFormat`": null,
      `"generateUpdateJsonSerializerSettingsMethod`": true,
      `"useItemConverter`": false,
      `"generateBaseClasses`": true,
      `"generateContractClasses`": false
    }
  }
}
"@

    $configFile = Join-Path $env:TEMP "nswag-config.json"
    $nswagConfig | Out-File -FilePath $configFile -Encoding UTF8

    try {
        nswag run $configFile
        if ($LASTEXITCODE -eq 0) {
            Write-Host "`n[OK] C# client generated successfully!" -ForegroundColor Green
            Write-Host "  Output: $OutputPath" -ForegroundColor Gray
        }
        else {
            throw "NSwag generation failed"
        }
    }
    catch {
        Write-Error "Failed to generate C# client with NSwag: $_"
        exit 1
    }
    finally {
        if (Test-Path $configFile) {
            Remove-Item $configFile -Force
        }
    }

}
elseif ($Tool -eq "openapi-generator") {
    # Check if openapi-generator is installed
    $openapiGeneratorInstalled = $false
    try {
        $version = openapi-generator version 2>&1
        if ($LASTEXITCODE -eq 0) {
            $openapiGeneratorInstalled = $true
            Write-Host "  Found openapi-generator: $version" -ForegroundColor Green
        }
    }
    catch {
        # openapi-generator not found
    }

    if (-not $openapiGeneratorInstalled) {
        Write-Host "`n[WARN] openapi-generator not found." -ForegroundColor Yellow
        Write-Host "  Install with: npm install -g @openapitools/openapi-generator-cli" -ForegroundColor Gray
        Write-Host "  Or use NSwag instead: -Tool nswag" -ForegroundColor Gray
        exit 1
    }

    # Generate client using openapi-generator
    Write-Host "`nGenerating C# client with openapi-generator..." -ForegroundColor Cyan
    
    $tempOutput = Join-Path $env:TEMP "openapi-client-temp"
    
    try {
        openapi-generator generate `
            -i $openapiFile `
            -g csharp-netcore `
            -o $tempOutput `
            --additional-properties=packageName=$($Namespace.Replace('.', '')), netCoreProjectFile=true, library=httpclient

        if ($LASTEXITCODE -eq 0) {
            # Copy generated file to target location
            $generatedFile = Get-ChildItem -Path $tempOutput -Recurse -Filter "*.cs" | Select-Object -First 1
            if ($generatedFile) {
                Copy-Item $generatedFile.FullName -Destination $OutputPath -Force
                Write-Host "`n[OK] C# client generated successfully!" -ForegroundColor Green
                Write-Host "  Output: $OutputPath" -ForegroundColor Gray
            }
            else {
                throw "Generated file not found"
            }
        }
        else {
            throw "openapi-generator generation failed"
        }
    }
    catch {
        Write-Error "Failed to generate C# client with openapi-generator: $_"
        exit 1
    }
    finally {
        if (Test-Path $tempOutput) {
            Remove-Item $tempOutput -Recurse -Force
        }
    }
}
else {
    Write-Error "Unknown tool: $Tool. Use 'nswag' or 'openapi-generator'"
    exit 1
}

Write-Host "`n[INFO] Next steps:" -ForegroundColor Cyan
Write-Host "  1. Review generated client: $OutputPath" -ForegroundColor Gray
Write-Host "  2. Create BackendClientAdapter.cs to wrap generated client" -ForegroundColor Gray
Write-Host "  3. Update ServiceProvider to use generated client" -ForegroundColor Gray
Write-Host "  4. Run contract tests to verify compatibility" -ForegroundColor Gray


