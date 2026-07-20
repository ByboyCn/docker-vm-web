namespace DockerVm.Dtos;

public class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public record UserDto(string Id, string Username, bool IsAdmin)
{
    public static UserDto From(Models.User u) => new(u.Id, u.Username, u.IsAdmin);
}
