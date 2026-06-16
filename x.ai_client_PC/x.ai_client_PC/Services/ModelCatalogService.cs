using System.Text.Json;
using x.ai_client_PC.Models;
using x.ai_client_PC.Services.Api;

namespace x.ai_client_PC.Services;

public class ModelCatalogService
{
    private readonly XaiApiClient _api;
    private readonly DataRepository _repo;

    private static readonly Dictionary<string, (double input, double output)> KnownTextPrices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["grok-4"] = (3.0, 15.0),
        ["grok-4-fast"] = (0.5, 2.0),
        ["grok-3"] = (3.0, 15.0),
        ["grok-3-mini"] = (0.3, 0.5),
        ["grok-2"] = (2.0, 10.0),
    };

    public ModelCatalogService(XaiApiClient api, DataRepository repo)
    {
        _api = api;
        _repo = repo;
    }

    public async Task<List<ModelInfo>> RefreshModelsAsync(CancellationToken ct = default)
    {
        var existing = await _repo.GetModelsAsync();
        var enabledMap = existing.ToDictionary(m => m.Id, m => m.IsEnabled, StringComparer.OrdinalIgnoreCase);
        var defaultText = existing.FirstOrDefault(m => m.IsDefault && m.Category == ModelCategory.Text)?.Id;
        var defaultImage = existing.FirstOrDefault(m => m.IsDefault && m.Category == ModelCategory.Image)?.Id;
        var defaultVideo = existing.FirstOrDefault(m => m.IsDefault && m.Category == ModelCategory.Video)?.Id;

        var models = new List<ModelInfo>();

        await TryLoadEndpointAsync("language-models", ModelCategory.Text, models, enabledMap, ct);
        await TryLoadEndpointAsync("image-generation-models", ModelCategory.Image, models, enabledMap, ct);
        await TryLoadEndpointAsync("video-generation-models", ModelCategory.Video, models, enabledMap, ct);

        if (models.Count == 0)
        {
            await TryLoadEndpointAsync("models", ModelCategory.Text, models, enabledMap, ct);
        }

        foreach (var model in models)
        {
            ApplyKnownPricing(model);
            if (defaultText is not null && model.Category == ModelCategory.Text && model.Id == defaultText)
            {
                model.IsDefault = true;
            }

            if (defaultImage is not null && model.Category == ModelCategory.Image && model.Id == defaultImage)
            {
                model.IsDefault = true;
            }

            if (defaultVideo is not null && model.Category == ModelCategory.Video && model.Id == defaultVideo)
            {
                model.IsDefault = true;
            }
        }

        if (!models.Any(m => m.IsDefault && m.Category == ModelCategory.Text))
        {
            var first = models.FirstOrDefault(m => m.Category == ModelCategory.Text);
            if (first is not null)
            {
                first.IsDefault = true;
            }
        }

        await _repo.SaveModelsAsync(models);
        return models;
    }

    private async Task TryLoadEndpointAsync(
        string endpoint,
        ModelCategory category,
        List<ModelInfo> models,
        Dictionary<string, bool> enabledMap,
        CancellationToken ct)
    {
        try
        {
            var remote = await _api.GetModelsAsync(endpoint, ct);
            foreach (var item in remote)
            {
                if (models.Any(m => m.Id == item.Id))
                {
                    continue;
                }

                var inferredCategory = InferCategory(item.Id, category);
                models.Add(new ModelInfo
                {
                    Id = item.Id,
                    DisplayName = item.Id,
                    Category = inferredCategory,
                    IsEnabled = enabledMap.GetValueOrDefault(item.Id, true),
                    SupportsImageInput = item.Id.Contains("vision", StringComparison.OrdinalIgnoreCase)
                        || item.Id.Contains("grok-4", StringComparison.OrdinalIgnoreCase),
                    SupportsReasoning = item.Id.Contains("grok-4", StringComparison.OrdinalIgnoreCase),
                    IsMultiAgent = XaiApiClient.IsMultiAgentModel(item.Id),
                    UsesResponsesApi = XaiApiClient.IsGrok4Family(item.Id),
                    RawJson = JsonSerializer.Serialize(item)
                });
            }
        }
        catch
        {
            // Fallback handled by caller
        }
    }

    private static ModelCategory InferCategory(string id, ModelCategory fallback)
    {
        if (id.Contains("video", StringComparison.OrdinalIgnoreCase) || id.Contains("imagine-video", StringComparison.OrdinalIgnoreCase))
        {
            return ModelCategory.Video;
        }

        if (id.Contains("image", StringComparison.OrdinalIgnoreCase) || id.Contains("imagine", StringComparison.OrdinalIgnoreCase))
        {
            return ModelCategory.Image;
        }

        return fallback;
    }

    private static void ApplyKnownPricing(ModelInfo model)
    {
        foreach (var (key, prices) in KnownTextPrices)
        {
            if (model.Id.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                model.InputPricePerMillion = prices.input;
                model.OutputPricePerMillion = prices.output;
                return;
            }
        }

        if (model.Category == ModelCategory.Image)
        {
            model.ImagePrice = 0.07;
        }

        if (model.Category == ModelCategory.Video)
        {
            model.VideoPricePerSecond = 0.05;
        }
    }
}