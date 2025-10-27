using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace CodePunk.Core.Utils;

public static class ToolArgumentBinder
{
    public static bool TryBindAndValidate<T>(JsonElement args, out T? value, out string? error)
    {
        value = default;
        error = null;
        try
        {
            value = JsonSerializer.Deserialize<T>(args);
            if (value == null)
            {
                error = "Unable to parse arguments";
                return false;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        var ctx = new ValidationContext(value!);
        var results = new List<ValidationResult>();
        var ok = Validator.TryValidateObject(value!, ctx, results, validateAllProperties: true);
        if (!ok)
        {
            error = string.Join("; ", results.Select(r => r.ErrorMessage).Where(m => !string.IsNullOrWhiteSpace(m)));
            return false;
        }
        return true;
    }
}

