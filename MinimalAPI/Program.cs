using Microsoft.EntityFrameworkCore;
using MinimalAPI.Data;
using Microsoft.EntityFrameworkCore.Design;
using MinimalAPI.Models;
using MiniValidation;
using NetDevPack.Identity;
using Microsoft.Extensions.Configuration;
using NetDevPack.Identity.Jwt;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using NetDevPack.Identity.Model;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<MinimalContextDb>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

builder.Services.AddIdentityEntityFrameworkContextConfiguration(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"),
    b => b.MigrationsAssembly("MinimalAPI")));

builder.Services.AddIdentityConfiguration();
builder.Services.AddJwtConfiguration(builder.Configuration, "AppSettings"); //Adiciona a configura��o do JWT pegando a key "AppSettings dentro do appsettings.json//

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ExcluirFornecedor",
        policy => policy.RequireClaim("ExcluirFornecedor"));
    options.AddPolicy("AdicionarFornecedor",
        policy => policy.RequireClaim("AdicionarFornecedor"));
    options.AddPolicy("AtualizarFornecedor",
    policy => policy.RequireClaim("AtualizarFornecedor"));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


app.UseAuthConfiguration();
app.UseHttpsRedirection();


// \/ AUTH API

app.MapPost("/registro", [AllowAnonymous] async (
        SignInManager<IdentityUser> signInManager,
        UserManager<IdentityUser> userManager,
        IOptions<AppJwtSettings> appJwtSettings,
        RegisterUser registerUser) =>
    {
        if (registerUser == null)
            return Results.BadRequest("Usu�rio n�o informado");

        if (!MiniValidator.TryValidate(registerUser, out var errors))
            return Results.ValidationProblem(errors);

        var user = new IdentityUser
        {
            UserName = registerUser.Email,
            Email = registerUser.Email,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, registerUser.Password);

        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);

        var jwt = new JwtBuilder()
                    .WithUserManager(userManager)
                    .WithJwtSettings(appJwtSettings.Value)
                    .WithEmail(user.Email)
                    .WithJwtClaims()
                    .WithUserClaims()
                    .WithUserRoles()
                    .BuildUserResponse();

        return Results.Ok(jwt);
    }).ProducesValidationProblem()
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .WithName("RegistroUsuario")
    .WithTags("Usuario");


app.MapPost("/login", [AllowAnonymous] async(
         SignInManager < IdentityUser > signInManager,
         UserManager < IdentityUser > userManager,
         IOptions < AppJwtSettings > appJwtSettings,
         LoginUser loginUser) =>
{
    if (loginUser == null)
        return Results.BadRequest("Usu�rio n�o informado");

    if (!MiniValidator.TryValidate(loginUser, out var erros))
        return Results.ValidationProblem(erros);

    var result = await signInManager.PasswordSignInAsync(loginUser.Email, loginUser.Password, false, true);

    if (result.IsLockedOut)
        return Results.BadRequest("Usu�rio bloqueado");

    if (!result.Succeeded)
        return Results.BadRequest("Usu�rio ou senha inv�lidos");

    var jwt = new JwtBuilder()
            .WithUserManager(userManager)
            .WithJwtSettings(appJwtSettings.Value)
            .WithEmail(loginUser.Email)
            .WithJwtClaims()
            .WithUserClaims()
            .WithUserRoles()
            .BuildUserResponse();

    return Results.Ok(jwt);

}).ProducesValidationProblem()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.WithName("LoginUsuario")
.WithTags("Usuario");


// \/ CRUD APIS

app.MapGet("/fornecedor", [AllowAnonymous] async (
     MinimalContextDb context) =>
 await context.Fornecedores.ToListAsync())
    .WithName("GetFornecedor")
    .WithTags("Fornecedor");

app.MapGet("/fornecedor/{id}", async (
     Guid id,
     MinimalContextDb context) =>
await context.Fornecedores.FindAsync(id)
        is Fornecedor fornecedor
            ? Results.Ok(fornecedor)
            : Results.NotFound())
    .Produces<Fornecedor>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .WithName("GetFornecedorPorId")
    .WithTags("Fornecedor");

app.MapPost("/fornecedor", [Authorize] async (
    MinimalContextDb context, Fornecedor fornecedor) =>
{
    if (!MiniValidator.TryValidate(fornecedor, out var errors))
        return Results.ValidationProblem(errors);

    context.Fornecedores.Add(fornecedor);
    var result = await context.SaveChangesAsync();
    return result > 0
    //? Results.Created($"/fornecedor/{fornecedor.Id}", fornecedor)
    ? Results.CreatedAtRoute("GetFornecedorPorId", new { id = fornecedor.Id }, fornecedor)
    : Results.BadRequest("Houve um problema ao salvar o registro");

})
    .ProducesValidationProblem()
    .Produces<Fornecedor>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest)
    .RequireAuthorization("AdicionarFornecedor")
    .WithName("PostFornecedor")
    .WithTags("Fornecedor");


app.MapPut("/fornecedor/{id}", [Authorize] async (
    Guid id,
    MinimalContextDb context, 
    Fornecedor fornecedor) =>
{
    var fornecedorBanco = await context.Fornecedores.AsNoTracking<Fornecedor>().
                                                     FirstOrDefaultAsync(f=>f.Id == id);
    if (fornecedorBanco == null) 
        return Results.NotFound();

    if (!MiniValidator.TryValidate(fornecedorBanco, out var errors))
        return Results.ValidationProblem(errors);

    context.Fornecedores.Update(fornecedor);
    var result = await context.SaveChangesAsync();

    return result > 0
        ? Results.NoContent()
        : Results.BadRequest("Houve um problema ao salvar o registro");

})
    .ProducesValidationProblem()
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status400BadRequest)
    .RequireAuthorization("AtualizarFornecedor")
    .WithName("PuFornecedor")
    .WithTags("Fornecedor");


app.MapDelete("/fornecedor/{id}", [Authorize] async (
    Guid id,
    MinimalContextDb context) =>
{
    var fornecedor = await context.Fornecedores.FindAsync(id);
    if (fornecedor == null)
        return Results.NotFound();

    if (!MiniValidator.TryValidate(fornecedor, out var errors))
        return Results.ValidationProblem(errors);

    context.Fornecedores.Remove(fornecedor);
    var result = await context.SaveChangesAsync();

    return result > 0
        ? Results.NoContent()
        : Results.BadRequest("Houve um problema ao salvar o registro");

})
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound)
    .RequireAuthorization("ExcluirFornecedor")
    .WithName("DeleteFornecedor")
    .WithTags("Fornecedor");

app.Run();

