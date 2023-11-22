using Couchbase;

using Couchbase.Extensions.DependencyInjection;
using Couchbase.KeyValue;
using Couchbase.TravelSample.Models;
using Microsoft.OpenApi.Models;

using Route = Couchbase.TravelSample.Models.Route;

var builder = WebApplication.CreateBuilder(args);
const string devSpecificOriginsName = "_devAllowSpecificOrigins";

//global pointer to inventory scope
IScope? inventoryScope = null;
IBucket? bucket = null;

const string airportCollection = "airport";
const string airlineCollection = "airline";
const string routeCollection = "route";

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Version = "1.0",
        Title = "Quickstart in Couchbase with C# and ASP.NET Minimum API",
        Description = "A quickstart API using C# and ASP.NET with Couchbase and travel-sample data. \n\n"
                      + "We have a visual representation of the API documentation using Swagger which allows you to interact with the API's endpoints directly through the browser. It provides a clear view of the API including endpoints, HTTP methods, request parameters, and response objects.\n\n"
                      + "Click on an individual endpoint to expand it and see detailed information. This includes the endpoint's description, possible response status codes, and the request parameters it accepts.\n\n"
                      + "Trying Out the API\n\n"
                      + "You can try out an API by clicking on the \"Try it out\" button next to the endpoints.\n\n"
                      + "- Parameters: If an endpoint requires parameters, Swagger UI provides input boxes for you to fill in. This could include path parameters, query strings, headers, or the body of a POST/PUT request.\n\n"
                      + "- Execution: Once you've inputted all the necessary parameters, you can click the \"Execute\" button to make a live API call. Swagger UI will send the request to the API and display the response directly in the documentation. This includes the response code, response headers, and response body.\n\n"
                      + "Models\n\n"
                      + "Swagger documents the structure of request and response bodies using models. These models define the expected data structure using JSON schema and are extremely helpful in understanding what data to send and expect.\n\n"
                      + "For details on the API, please check the tutorial on the Couchbase Developer Portal: https://developer.couchbase.com/tutorial-quickstart-csharp-aspnet"
    });
});

var config = builder.Configuration.GetSection("Couchbase");

//register the configuration for Couchbase and Dependency Injection Framework
if (builder.Environment.EnvironmentName == "Testing")
{
    var connectionString = Environment.GetEnvironmentVariable("DB_CONN_STR");
    var username = Environment.GetEnvironmentVariable("DB_USERNAME");
    var password = Environment.GetEnvironmentVariable("DB_PASSWORD");
    config["ConnectionString"] = connectionString;
    config["Username"] = username;
    config["Password"] = password;
    
    builder.Services.Configure<CouchbaseConfig>(config);
    builder.Services.AddCouchbase(config);
}

else
{
    builder.Services.Configure<CouchbaseConfig>(config);
    builder.Services.AddCouchbase(config);
}

builder.Services.AddHttpClient();

//fix for debugging dev and qa environments in Github 
//DO NOT APPLY to UAT or Production Environments!!!
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: devSpecificOriginsName,
        corsPolicyBuilder =>
        {
            corsPolicyBuilder.WithOrigins("https://*.github.com",
                    "http://localhost:5000",
                    "http://localhost:8080",
                    "https://localhost:5001")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
});

var app = builder.Build();

app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.Register(async () =>
{
    var configuration = builder.Configuration;

    // Retrieve configuration values from appsettings.json
    var bucketName = configuration["Couchbase:BucketName"];
    if (bucketName == null) return;
    bucket = await app.Services.GetRequiredService<IBucketProvider>().GetBucketAsync(bucketName);

    const string scopeName = "inventory";
    
    // get inventory scope
    try
    {
        inventoryScope = await bucket.ScopeAsync(scopeName);
    }
    catch (Exception){
        Console.WriteLine("Warning: The 'inventory' scope does not exist in 'travel-sample' bucket.");
    }
});

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI();

if (app.Environment.EnvironmentName == "Testing")
{
    app.UseCors(devSpecificOriginsName);
}

//remove couchbase from memory when ASP.NET closes
app.Lifetime.ApplicationStopped.Register(() =>
{
    var cls = app.Services.GetRequiredService<ICouchbaseLifetimeService>();
    cls.Close();
});

app.UseHttpsRedirection();

app.MapGet("/api/v1/airport/list", async (string? country, int? limit, int? offset) =>
    {
        try
        {
            if (inventoryScope is not null){

                // Set default values for limit and offset if not provided by the user
                limit ??= 10; 
                offset ??= 0;

                var query = string.IsNullOrEmpty(country) ? $@"SELECT airport.airportname,
                              airport.city,
                              airport.country,
                              airport.faa,
                              airport.geo,
                              airport.icao,
                              airport.tz
                 FROM airport AS airport
                 ORDER BY airport.airportname
                 LIMIT $limit
                 OFFSET $offset" : $@"SELECT airport.airportname,
                          airport.city,
                          airport.country,
                          airport.faa,
                          airport.geo,
                          airport.icao,
                          airport.tz
             FROM airport AS airport
             WHERE lower(airport.country) = $country
             ORDER BY airport.airportname
             LIMIT $limit
             OFFSET $offset";

                //setup parameters
                var queryParameters = new Couchbase.Query.QueryOptions();
                queryParameters.Parameter("country", string.IsNullOrEmpty(country) ? "" : country.ToLower());
                queryParameters.Parameter("limit", limit);
                queryParameters.Parameter("offset", offset);

                var results = await inventoryScope.QueryAsync<Airport>(query, queryParameters);
                var items = await results.Rows.ToListAsync();

                return items.Count == 0 ? Results.NotFound() : Results.Ok(items);
            }
            else
            {
                return Results.Problem("Scope not found");
            }
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    })
    .WithTags("Airport")
    .WithOpenApi(operation => new(operation)
    {
        Description = "Get list of Airports. Optionally, you can filter the list by Country.\n\nThis provides an example of using a SQL++ query in Couchbase to fetch a list of documents matching the specified criteria.",
        Parameters = new List<OpenApiParameter>
        {
            new OpenApiParameter
            {
                Name = "country",
                In = ParameterLocation.Query,
                Description = "Country (Example: United Kingdom, France, United States)",
                Required = false
            },
            new OpenApiParameter
            {
                Name = "limit",
                In = ParameterLocation.Query,
                Description = "Number of airports to return (page size). Default value: 10.",
                Required = false
            },
            new OpenApiParameter
            {
                Name = "offset",
                In = ParameterLocation.Query,
                Description = "Number of airports to skip (for pagination). Default value: 0.",
                Required = false
            }
        }
    });


app.MapGet("/api/v1/airport/direct-connections", async (string airport, int? limit, int? offset) =>
    {
        try
        {
            if (inventoryScope is not null)
            {
                // Set default values for limit and offset if not provided by the user
                limit ??= 10;
                offset ??= 0;

                const string query = $@"SELECT DISTINCT route.destinationairport
                 FROM airport AS airport
                 JOIN route AS route
                 ON route.sourceairport = airport.faa
                 WHERE lower(airport.faa) = $airport AND route.stops = 0
                 ORDER BY route.destinationairport
                 LIMIT $limit
                 OFFSET $offset";

                //setup parameters
                var queryParameters = new Couchbase.Query.QueryOptions();
                queryParameters.Parameter("airport", airport.ToLower());
                queryParameters.Parameter("limit", limit);
                queryParameters.Parameter("offset", offset);

                var results = await inventoryScope.QueryAsync<DestinationAirport>(query, queryParameters);
                var items = await results.Rows.ToListAsync();

                return items.Count == 0 ? Results.NotFound() : Results.Ok(items);
            }
            else
            {
                return Results.Problem("Scope not found");
            }
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    })
    .WithTags("Airport")
    .WithOpenApi(operation => new(operation)
    {
        Description = "Get Direct Connections from specified Airport.\n\nThis provides an example of using a SQL++ query in Couchbase to fetch a list of documents matching the specified criteria.",
        Parameters = new List<OpenApiParameter>
        {
            new OpenApiParameter
            {
                Name = "airport",
                In = ParameterLocation.Query,
                Description = "Source airport (Example: SFO, LHR, CDG)",
                Required = true
            },
            new OpenApiParameter
            {
                Name = "limit",
                In = ParameterLocation.Query,
                Description = "Number of direct connections to return (page size). Default value: 10.",
                Required = false
            },
            new OpenApiParameter
            {
                Name = "offset",
                In = ParameterLocation.Query,
                Description = "Number of direct connections to skip (for pagination). Default value: 0.",
                Required = false
            }
        }
    });


app.MapGet("/api/v1/airport/{id}", async (string id) =>
    {
        try
        {
            if (inventoryScope is not null)
            {
                //get the collection
                var collection = inventoryScope.Collection(airportCollection);

                //get the document from the bucket using the id
                var result = await collection.GetAsync(id);

                //validate we have a document
                var resultAirports = result.ContentAs<Airport>();
                if (resultAirports != null)
                {
                    return Results.Ok(resultAirports);
                }
            }
            else
            {
                return Results.Problem("Scope not found");
            }
        }
        catch (Couchbase.Core.Exceptions.KeyValue.DocumentNotFoundException)
        {
            Results.NotFound();
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }

        return Results.NotFound();
    })
    .WithTags("Airport")
    .WithOpenApi(operation => new(operation)
    {
        Description = "Get Airport with specified ID.\n\nThis provides an example of using Key Value operations in Couchbase to get a document with specified ID.",
        Parameters = new List<OpenApiParameter>
        {
            new OpenApiParameter
            {
                Name = "id",
                In = ParameterLocation.Path,
                Description = "Airport ID like airport_1273",
                Required = true
            }
        }
    });

app.MapPost("/api/v1/airport/{id}", async (string id, AirportCreateRequestCommand request) =>
    {
        if (inventoryScope is not null)
        {
            //get the collection
            var collection = inventoryScope.Collection(airportCollection);

            //get airport from request
            var airport = request.GetAirport();

            //save document
            await collection.InsertAsync(id, airport);
            return Results.Created($"/api/v1/airport/{id}", airport);
        }
        else
        {
            return Results.Problem("Scope not found");
        }
    })
    .WithTags("Airport")
    .WithOpenApi(operation => new(operation)
    {
        Description = "Create Airport with specified ID.\n\nThis provides an example of using Key Value operations in Couchbase to create a new document with a specified ID.",
        Parameters = new List<OpenApiParameter>
        {
            new OpenApiParameter
            {
                Name = "id",
                In = ParameterLocation.Path,
                Description = "Airport ID like airport_1273",
                Required = true
            }
        }
    });

app.MapPut("/api/v1/airport/{id}", async (string id, AirportCreateRequestCommand request) =>
    {
        if (inventoryScope is not null)
        {
            //get the collection
            var collection = inventoryScope.Collection(airportCollection);

            //get current airport from the database and update it
            if (await collection.GetAsync(id) is { } result)
            {
                result.ContentAs<Airport>();
                await collection.ReplaceAsync(id, request.GetAirport());
                return Results.Ok(request);
            }
            else
            {
                return Results.NotFound();
            }
        }
        else
        {
            return Results.Problem("Scope not found");
        }
    })
    .WithTags("Airport")
    .WithOpenApi(operation => new(operation)
    {
        Description = "Update Airport with specified ID.\n\nThis provides an example of using Key Value operations in Couchbase to upsert a document with specified ID.",
        Parameters = new List<OpenApiParameter>
        {
            new OpenApiParameter
            {
                Name = "id",
                In = ParameterLocation.Path,
                Description = "Airport ID like airport_1273",
                Required = true
            }
        }
    });

app.MapDelete("/api/v1/airport/{id}", async(string id) => 
    {
        if (inventoryScope is not null)
        {
            //get the collection
            var collection = inventoryScope.Collection(airportCollection);

            //get the document from the bucket using the id
            var result = await collection.GetAsync(id);

            //validate we have a document
            var resultAirport = result.ContentAs<Airport>();
            if (resultAirport != null)
            {
                await collection.RemoveAsync(id);
                return Results.Ok(id);
            }
            else
            {
                return Results.NotFound();
            }
        }
        else
        {
            return Results.Problem("Scope not found");
        }
    })
    .WithTags("Airport")
    .WithOpenApi(operation => new(operation)
    {
        Description = "Delete Airport with specified ID.\n\nThis provides an example of using Key Value operations in Couchbase to delete a document with specified ID.",
        Parameters = new List<OpenApiParameter>
        {
            new OpenApiParameter
            {
                Name = "id",
                In = ParameterLocation.Path,
                Description = "Airport ID like airport_1273",
                Required = true
            }
        }
    });

app.MapGet("/api/v1/airline/list", async (string? country, int? limit, int? offset) =>
    {
        try
        {
            if (inventoryScope is not null)
            {
                // Set default values for limit and offset if not provided by the user
                limit ??= 10;
                offset ??= 0;

                var query = string.IsNullOrEmpty(country)
                    ? $@"SELECT airline.callsign,
                            airline.country,
                            airline.iata,
                            airline.icao,
                            airline.name
                            FROM airline AS airline
                            ORDER BY airline.name
                            LIMIT $limit
                            OFFSET $offset"
                    : $@"SELECT airline.callsign,
                            airline.country,
                            airline.iata,
                            airline.icao,
                            airline.name
                            FROM airline AS airline
                            WHERE lower(airline.country) = $country
                            ORDER BY airline.name
                            LIMIT $limit
                            OFFSET $offset";

                //setup parameters
                var queryParameters = new Couchbase.Query.QueryOptions();
                queryParameters.Parameter("country", string.IsNullOrEmpty(country) ? "" : country.ToLower());
                queryParameters.Parameter("limit", limit);
                queryParameters.Parameter("offset", offset);

                var results = await inventoryScope.QueryAsync<Airline>(query, queryParameters);
                var items = await results.Rows.ToListAsync();

                return items.Count == 0 ? Results.NotFound() : Results.Ok(items);
            }
            else
            {
                return Results.Problem("Scope not found");
            }
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    })
    .WithTags("Airline")
    .WithOpenApi(operation => new(operation)
    {
        Description = "Get list of Airlines. Optionally, you can filter the list by Country.\n\nThis provides an example of using SQL++ query in Couchbase to fetch a list of documents matching the specified criteria.",
        Parameters = new List<OpenApiParameter>
        {
            new OpenApiParameter
            {
                Name = "country",
                In = ParameterLocation.Query,
                Description = "Country (Example: France, United Kingdom, United States)",
                Required = false
            },
            new OpenApiParameter
            {
                Name = "limit",
                In = ParameterLocation.Query,
                Description = "Number of airlines to return (page size). Default value: 10.",
                Required = false
            },
            new OpenApiParameter
            {
                Name = "offset",
                In = ParameterLocation.Query,
                Description = "Number of airlines to skip (for pagination). Default value: 0.",
                Required = false
            }
        }
        
    });

app.MapGet("/api/v1/airline/to-airport", async (string airport, int? limit, int? offset) =>
    {
        try
        {
            if (inventoryScope is not null)
            {
                // Set default values for limit and offset if not provided by the user
                limit ??= 10;
                offset ??= 0;

                var query = $@"SELECT air.callsign,
                                   air.country,
                                   air.iata,
                                   air.icao,
                                   air.name
                          FROM (
                            SELECT DISTINCT META(airline).id AS airlineId
                            FROM route AS route
                            JOIN airline AS airline
                            ON route.airlineid = META(airline).id
                            WHERE lower(route.destinationairport) = $airport
                          ) AS SUBQUERY
                          JOIN airline AS air
                          ON META(air).id = SUBQUERY.airlineId
                          LIMIT $limit
                          OFFSET $offset";

                //setup parameters
                var queryParameters = new Couchbase.Query.QueryOptions();
                queryParameters.Parameter("airport", airport.ToLower());
                queryParameters.Parameter("limit", limit);
                queryParameters.Parameter("offset", offset);

                var results = await inventoryScope.QueryAsync<Airline>(query, queryParameters);
                var items = await results.Rows.ToListAsync();

                return items.Count == 0 ? Results.NotFound() : Results.Ok(items);
            }
            else
            {
                return Results.Problem("Scope not found");
            }
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    })
    .WithTags("Airline")
    .WithOpenApi(operation => new(operation)
    {
        Description = "Get Airlines flying to specified destination Airport.\n\nThis provides an example of using SQL++ query in Couchbase to fetch a list of documents matching the specified criteria.",
        Parameters = new List<OpenApiParameter>
        {
            new OpenApiParameter
            {
                Name = "airport",
                In = ParameterLocation.Query,
                Description = "Destination airport (Example: SFO, JFK, LAX)",
                Required = true
            },
            new OpenApiParameter
            {
                Name = "limit",
                In = ParameterLocation.Query,
                Description = "Number of airlines to return (page size). Default value: 10.",
                Required = false
            },
            new OpenApiParameter
            {
                Name = "offset",
                In = ParameterLocation.Query,
                Description = "Number of airlines to skip (for pagination). Default value: 0.",
                Required = false
            }
        }
    });

app.MapGet("/api/v1/airline/{id}", async (string id) =>
    {
        try
        {
            if (inventoryScope is not null)
            {
                //get the collection
                var collection = inventoryScope.Collection(airlineCollection);

                //get the document from the bucket using the id
                var result = await collection.GetAsync(id);

                //validate we have a document
                var resultAirlines = result.ContentAs<Airline>();
                if (resultAirlines != null)
                {
                    return Results.Ok(resultAirlines);
                }
            }
            else
            {
                return Results.Problem("Scope Not Found");
            }
        }
        catch (Couchbase.Core.Exceptions.KeyValue.DocumentNotFoundException)
        {
            Results.NotFound();
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }

        return Results.NotFound();
    })
    .WithTags("Airline")
    .WithOpenApi(operation => new(operation)
    {
        Description = "Get Airline with specified ID.\n\nThis provides an example of using Key Value operations in Couchbase to get a document with specified ID.",
        Parameters = new List<OpenApiParameter>
        {
            new OpenApiParameter
            {
                Name = "id",
                In = ParameterLocation.Path,
                Description = "Airline ID like airline_10",
                Required = true
            }
        }
    });

app.MapPost("/api/v1/airline/{id}", async (string id, AirlineCreateRequestCommand request) =>
    {
        if (inventoryScope is not null)
        {
            //get the collection
            var collection = inventoryScope.Collection(airlineCollection);

            //get airline from request
            var airline = request.GetAirline();

            //save document
            await collection.InsertAsync(id, airline);
            return Results.Created($"/api/v1/airline/{id}", airline);
        }
        else
        {
            return Results.Problem("Scope Not Found");
        }
    })
    .WithTags("Airline")
    .WithOpenApi(operation => new(operation)
    {
        Description = "Create Airline with specified ID.\n\nThis provides an example of using Key Value operations in Couchbase to create a new document with a specified ID.",
        Parameters = new List<OpenApiParameter>
        {
            new OpenApiParameter
            {
                Name = "id",
                In = ParameterLocation.Path,
                Description = "Airline ID like airline_10",
                Required = true
            }
        }
    });

app.MapPut("/api/v1/airline/{id}", async (string id, AirlineCreateRequestCommand request) =>
    {
        if (inventoryScope is not null)
        {
            //get the collection
            var collection = inventoryScope.Collection(airlineCollection);

            //get current airline from the database and update it
            if (await collection.GetAsync(id) is { } result)
            {
                result.ContentAs<Airline>();
                await collection.ReplaceAsync(id, request.GetAirline());
                return Results.Ok(request);
            }
            else
            {
                return Results.NotFound();
            }
        }
        else
        {
            return Results.Problem("Scope Not Found");
        }
    })
    .WithTags("Airline")
    .WithOpenApi(operation => new(operation)
    {
        Description = "Update Airline with specified ID.\n\nThis provides an example of using Key Value operations in Couchbase to upsert a document with specified ID.",
        Parameters = new List<OpenApiParameter>
        {
            new OpenApiParameter
            {
                Name = "id",
                In = ParameterLocation.Path,
                Description = "Airline ID like airline_10",
                Required = true
            }
        }
    });

app.MapDelete("/api/v1/airline/{id}", async(string id) => 
    {
        if (inventoryScope is not null)
        {
            //get the collection
            var collection = inventoryScope.Collection(airlineCollection);

            //get the document from the bucket using the id
            var result = await collection.GetAsync(id);

            //validate we have a document
            var resultAirline = result.ContentAs<Airline>();
            if ( resultAirline != null)
            {
                await collection.RemoveAsync(id);
                return Results.Ok(id);
            }
            else
            {
                return Results.NotFound();
            }
        }
        else
        {
            return Results.Problem("Scope Not Found");
        }
    })
    .WithTags("Airline")
    .WithOpenApi(operation => new(operation)
    {
        Description = "Delete Airline with specified ID.\n\nThis provides an example of using Key Value operations in Couchbase to delete a document with specified ID.",
        Parameters = new List<OpenApiParameter>
        {
            new OpenApiParameter
            {
                Name = "id",
                In = ParameterLocation.Path,
                Description = "Airline ID like airline_10",
                Required = true
            }
        }
    });

app.MapGet("/api/v1/route/{id}", async (string id) =>
    {
        try
        {
            if (inventoryScope is not null)
            {
                //get the collection
                var collection = inventoryScope.Collection(routeCollection);

                //get the document from the bucket using the id
                var result = await collection.GetAsync(id);

                //validate we have a document
                var resultAirlines = result.ContentAs<Route>();
                if (resultAirlines != null)
                {
                    return Results.Ok(resultAirlines);
                }
            }
            else
            {
                return Results.Problem("Scope Not Found");
            }
        }
        catch (Couchbase.Core.Exceptions.KeyValue.DocumentNotFoundException)
        {
            Results.NotFound();
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }

        return Results.NotFound();
    })
    .WithTags("Route")
    .WithOpenApi(operation => new(operation)
    {
        Description = "Get Route with specified ID.\n\nThis provides an example of using Key Value operations in Couchbase to get a document with specified ID.",
        Parameters = new List<OpenApiParameter>
        {
            new OpenApiParameter
            {
                Name = "id",
                In = ParameterLocation.Path,
                Description = "Route ID like route_10000",
                Required = true
            }
        }
    });

app.MapPost("/api/v1/route/{id}", async (string id, RouteCreateRequestCommand request) =>
    {
        if (inventoryScope is not null)
        {
            //get the collection
            var collection = inventoryScope.Collection(routeCollection);

            //get route from request
            var route = request.GetRoute();

            //save document
            await collection.InsertAsync(id, route);
            return Results.Created($"/api/v1/route/{id}", route);
        }
        else
        {
            return Results.Problem("Scope Not Found");
        }
    })
    .WithTags("Route")
    .WithOpenApi(operation => new(operation)
    {
        Description = "Create Route with specified ID.\n\nThis provides an example of using Key Value operations in Couchbase to create a new document with a specified ID.",
        Parameters = new List<OpenApiParameter>
        {
            new OpenApiParameter
            {
                Name = "id",
                In = ParameterLocation.Path,
                Description = "Route ID like route_10000",
                Required = true
            }
        }
    });

app.MapPut("/api/v1/route/{id}", async (string id,RouteCreateRequestCommand request) =>
    {
        if (inventoryScope is not null)
        {
            //get the collection
            var collection = inventoryScope.Collection(routeCollection);

            //get current route from the database and update it
            if (await collection.GetAsync(id) is { } result)
            {
                result.ContentAs<Route>();
                await collection.ReplaceAsync(id, request.GetRoute());
                return Results.Ok(request);
            }
            else
            {
                return Results.NotFound();
            } 
        }
        else
        {
            return Results.Problem("Scope Not Found");
        }
        
    })
    .WithTags("Route")
    .WithOpenApi(operation => new(operation)
    {
        Description = "Update Route with specified ID.\n\nThis provides an example of using Key Value operations in Couchbase to upsert a document with specified ID.",
        Parameters = new List<OpenApiParameter>
        {
            new OpenApiParameter
            {
                Name = "id",
                In = ParameterLocation.Path,
                Description = "Route ID like route_10000",
                Required = true
            }
        }
    });

app.MapDelete("/api/v1/route/{id}", async(string id) => 
    {
        if (inventoryScope is not null)
        {
            var collection = inventoryScope.Collection(routeCollection);

            //get the document from the bucket using the id
            var result = await collection.GetAsync(id);

            //validate we have a document
            var resultRoute = result.ContentAs<Route>();
            if ( resultRoute != null)
            {
                await collection.RemoveAsync(id);
                return Results.Ok(id);
            }
            else
            {
                return Results.NotFound();
            }
        }
        else
        {
            return Results.Problem("Scope Not Found");
        }
     
    })
    .WithTags("Route")
    .WithOpenApi(operation => new(operation)
    {
        Description = "Delete Route with specified ID.\n\nThis provides an example of using Key Value operations in Couchbase to delete a document with specified ID.",
        Parameters = new List<OpenApiParameter>
        {
            new OpenApiParameter
            {
                Name = "id",
                In = ParameterLocation.Path,
                Description = "Route ID like route_10000",
                Required = true
            }
        }
    });

app.Run();

// required for integration testing from asp.net
// https://docs.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-7.0
public abstract partial class Program { }

