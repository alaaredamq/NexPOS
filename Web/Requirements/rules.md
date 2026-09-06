# Coding rules
do not straight ef core operations like db.products....
entities should be fetched and managed through repositories, each inheriting from the base repository, and repositories will be managed through services which will be called in controllers
# Example
```csharp
// Bad
var products = db.Products.Where(p => p.Price > 100).ToList();
// Good
var products = await _productService.GetProductsByPriceAsync(100);
``` 
# Explanation
This rule promotes better separation of concerns, maintainability, and testability in the codebase by using repositories and services to handle data operations instead of directly interacting with the Entity Framework Core context in controllers
# References
- [Repository Pattern](https://martinfowler.com/eaaCatalog/repository.html)
- [Service Layer Pattern](https://martinfowler.com/eaaCatalog/serviceLayer.html)
- [Entity Framework Core Documentation](https://docs.microsoft.com/en-us/ef/core/)
- [Clean Architecture](https://8thlight.com/blog/uncle-bob/2012/08/13/the-clean-architecture.html)
- [ASP.NET Core Documentation](https://docs.microsoft.com/en-us/aspnet/core/?view=aspnetcore-6.0)
- [Dependency Injection in ASP.NET Core](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection)
- [Unit Testing with Repositories and Services](https://docs.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices)
- [Design Patterns in C#](https://refactoring.guru/design-patterns/csharp)
- [Domain-Driven Design](https://www.domainlanguage.com/ddd/)

# Tags
- ef core
- repository pattern
- service layer
- separation of concerns
- maintainability
- testability
- asp.net core
- dependency injection
- unit testing
- clean architecture

