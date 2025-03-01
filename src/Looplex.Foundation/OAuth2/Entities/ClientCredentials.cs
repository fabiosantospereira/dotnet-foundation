﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Looplex.Foundation.Entities;
using Looplex.Foundation.Ports;
using Looplex.Foundation.SCIMv2.Entities;
using Looplex.Foundation.Serialization;
using Looplex.OpenForExtension.Abstractions.Commands;
using Looplex.OpenForExtension.Abstractions.ExtensionMethods;
using Looplex.OpenForExtension.Abstractions.Plugins;
using Microsoft.Extensions.Configuration;
using Org.BouncyCastle.Crypto.Generators;

namespace Looplex.Foundation.OAuth2.Entities;

public class ClientCredentials : Service, IClientCredentials
{
    private readonly IRbacService _rbacService;
    private readonly IConfiguration _configuration;
    private readonly IJsonSchemaProvider _jsonSchemaProvider;
    private readonly IUserContext _userContext;

    #region Reflectivity

    // ReSharper disable once PublicConstructorInAbstractClass
    public ClientCredentials() : base()
    {
    }

    #endregion
    
    public ClientCredentials(IList<IPlugin> plugins,
        IRbacService rbacService,
        IConfiguration configuration,
        IJsonSchemaProvider jsonSchemaProvider,
        IUserContext userContext) : base(plugins)
    {
        _rbacService = rbacService;
        _configuration = configuration;
        _jsonSchemaProvider = jsonSchemaProvider;
        _userContext = userContext;
    }
    
    private const string JsonSchemaIdForClientCredentialKey = "JsonSchemaIdForClientCredential";

    internal static readonly IList<ClientCredential> Data = [];

    public async Task<string> QueryAsync(int startIndex, int itemsPerPage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ctx = NewContext();
        _rbacService!.ThrowIfUnauthorized(_userContext!, GetType().Name, this.GetCallerName());

        await ctx.Plugins.ExecuteAsync<IHandleInput>(ctx, cancellationToken);

        await ctx.Plugins.ExecuteAsync<IValidateInput>(ctx, cancellationToken);

        await ctx.Plugins.ExecuteAsync<IDefineRoles>(ctx, cancellationToken);

        await ctx.Plugins.ExecuteAsync<IBind>(ctx, cancellationToken);

        await ctx.Plugins.ExecuteAsync<IBeforeAction>(ctx, cancellationToken);

        if (!ctx.SkipDefaultAction)
        {
            var records = Data
                .Skip(Math.Min(0, startIndex - 1))
                .Take(itemsPerPage)
                .ToList();

            var result = new ListResponse<ClientCredential>
            {
                Resources = records.Select(r => r).ToList(),
                StartIndex = startIndex,
                ItemsPerPage = itemsPerPage,
                TotalResults = Data.Count
            };

            ctx.Result = result.JsonSerialize();
        }

        await ctx.Plugins.ExecuteAsync<IAfterAction>(ctx, cancellationToken);

        await ctx.Plugins.ExecuteAsync<IReleaseUnmanagedResources>(ctx, cancellationToken);

        return (string)ctx.Result;
    }
    
    public async Task<string> RetrieveAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ctx = NewContext();
        _rbacService!.ThrowIfUnauthorized(_userContext!, GetType().Name, this.GetCallerName());

        await ctx.Plugins.ExecuteAsync<IHandleInput>(ctx, cancellationToken);

        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentNullException(nameof(id));
        var clientCredential = Data.FirstOrDefault(c => c.Id == id);
        if (clientCredential == null)
        {
            throw new Exception($"{nameof(ClientCredential)} with id {id} not found,");
        }
        await ctx.Plugins.ExecuteAsync<IValidateInput>(ctx, cancellationToken);

        ctx.Roles.Add("ClientCredential", clientCredential);
        await ctx.Plugins.ExecuteAsync<IDefineRoles>(ctx, cancellationToken);

        await ctx.Plugins.ExecuteAsync<IBind>(ctx, cancellationToken);

        await ctx.Plugins.ExecuteAsync<IBeforeAction>(ctx, cancellationToken);

        if (!ctx.SkipDefaultAction)
        {
            ctx.Result = ((ClientCredential)ctx.Roles["ClientCredential"]).JsonSerialize();
        }

        await ctx.Plugins.ExecuteAsync<IAfterAction>(ctx, cancellationToken);

        await ctx.Plugins.ExecuteAsync<IReleaseUnmanagedResources>(ctx, cancellationToken);

        return (string)ctx.Result;
    }
    
    public async Task<string> CreateAsync(string json, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ctx = NewContext();
        _rbacService!.ThrowIfUnauthorized(_userContext!, GetType().Name, this.GetCallerName());

        var clientCredential = json.JsonDeserialize<ClientCredential>();
        await ctx.Plugins.ExecuteAsync<IHandleInput>(ctx, cancellationToken);

        await ctx.Plugins.ExecuteAsync<IValidateInput>(ctx, cancellationToken);

        ctx.Roles.Add("ClientCredential", clientCredential);
        await ctx.Plugins.ExecuteAsync<IDefineRoles>(ctx, cancellationToken);

        await ctx.Plugins.ExecuteAsync<IBind>(ctx, cancellationToken);

        await ctx.Plugins.ExecuteAsync<IBeforeAction>(ctx, cancellationToken);

        if (!ctx.SkipDefaultAction)
        {
            clientCredential = (ClientCredential)ctx.Roles["ClientCredential"];

            var clientId = Guid.NewGuid().ToString();

            var clientSecretByteLength = int.Parse(_configuration!["ClientSecretByteLength"]!);

            var clientSecretBytes = new byte[clientSecretByteLength];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(clientSecretBytes);
            }

            clientCredential.ClientId = clientId;
            clientCredential.Digest = DigestCredentials(clientId, clientSecretBytes)!;

            clientCredential.Id = Guid.NewGuid().ToString(); // This should be generated by the DB
            Data.Add(clientCredential); // Persist in storage

            ctx.Result = clientCredential.Id;
        }

        await ctx.Plugins.ExecuteAsync<IAfterAction>(ctx, cancellationToken);

        await ctx.Plugins.ExecuteAsync<IReleaseUnmanagedResources>(ctx, cancellationToken);

        return (string)ctx.Result;
    }

    private string DigestCredentials(string clientId, byte[] clientSecretBytes)
    {
        string digest;

        try
        {
            var clientSecretDigestCost = int.Parse(_configuration!["ClientSecretDigestCost"]!);

            digest = Convert.ToBase64String(BCrypt.Generate(
                clientSecretBytes,
                Encoding.UTF8.GetBytes(clientId),
                clientSecretDigestCost));
        }
        catch (Exception)
        {
            digest = null;
        }

        return digest;
    }

    public Task<string> UpdateAsync(string id, string json, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
    
    public Task<string> PatchAsync(string id, string json, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
    
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ctx = NewContext();
        _rbacService!.ThrowIfUnauthorized(_userContext!, GetType().Name, this.GetCallerName());

        await ctx.Plugins.ExecuteAsync<IHandleInput>(ctx, cancellationToken);

        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentNullException(nameof(id));
        var clientCredential = Data.FirstOrDefault(c => c.Id == id);
        if (clientCredential == null)
        {
            throw new Exception($"{nameof(ClientCredential)} with id {id} not found,");
        }
        await ctx.Plugins.ExecuteAsync<IValidateInput>(ctx, cancellationToken);

        ctx.Roles.Add("ClientCredential", clientCredential);
        await ctx.Plugins.ExecuteAsync<IDefineRoles>(ctx, cancellationToken);

        await ctx.Plugins.ExecuteAsync<IBind>(ctx, cancellationToken);

        await ctx.Plugins.ExecuteAsync<IBeforeAction>(ctx, cancellationToken);

        if (!ctx.SkipDefaultAction)
        {
            ctx.Result = Data.Remove(ctx.Roles["ClientCredential"]);
        }

        await ctx.Plugins.ExecuteAsync<IAfterAction>(ctx, cancellationToken);

        await ctx.Plugins.ExecuteAsync<IReleaseUnmanagedResources>(ctx, cancellationToken);

        return (bool)ctx.Result;
    }
    
    public async Task<string> RetrieveAsync(string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ctx = NewContext();
        _rbacService!.ThrowIfUnauthorized(_userContext!, GetType().Name, this.GetCallerName());

        var digest = DigestCredentials(clientId, Convert.FromBase64String(clientSecret));
        var clientCredential = Data.FirstOrDefault(c => c.Digest == digest);
        await ctx.Plugins.ExecuteAsync<IHandleInput>(ctx, cancellationToken);

        await ctx.Plugins.ExecuteAsync<IValidateInput>(ctx, cancellationToken);

        if (clientCredential != null && digest != null)
        {
            ctx.Roles.Add("ClientCredential", clientCredential);
        }
        await ctx.Plugins.ExecuteAsync<IDefineRoles>(ctx, cancellationToken);

        await ctx.Plugins.ExecuteAsync<IBind>(ctx, cancellationToken);

        await ctx.Plugins.ExecuteAsync<IBeforeAction>(ctx, cancellationToken);

        if (!ctx.SkipDefaultAction)
        {
            if (ctx.Roles.TryGetValue("ClientCredential", out var role))
            {
                ctx.Result = ((ClientCredential)role).JsonSerialize();
            }
            ctx.Result = Data.Remove(ctx.Roles["ClientCredential"]);
        }

        await ctx.Plugins.ExecuteAsync<IAfterAction>(ctx, cancellationToken);

        await ctx.Plugins.ExecuteAsync<IReleaseUnmanagedResources>(ctx, cancellationToken);

        return (string)ctx.Result;
    }
}