namespace BookingService.Api.Middleware;

/// <summary>
/// Extension methods for registering custom middleware in the application pipeline.
/// </summary>
public static class MiddlewareExtensions
{
    /// <summary>
    /// Adds the GlobalExceptionMiddleware to the application request pipeline.
    /// </summary>
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GlobalExceptionMiddleware>();
    }
}
