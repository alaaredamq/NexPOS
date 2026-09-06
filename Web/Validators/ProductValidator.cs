using Common.Helpers;
using Core.Entities.Products;
using FluentValidation;

namespace Web.Validators;

public class ProductValidator : AbstractValidator<Product>
{
    public ProductValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Product name is required")
            .MaximumLength(200).WithMessage("Product name cannot exceed 200 characters");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Product description cannot exceed 1000 characters");

        RuleFor(x => x.Barcode)
            .NotEmpty().WithMessage("Barcode is required")
            .MaximumLength(50).WithMessage("Barcode cannot exceed 50 characters");

        RuleFor(x => x.SellingPrice)
            .GreaterThan(0).WithMessage("Selling price must be greater than 0")
            .LessThan(1000000).WithMessage("Selling price cannot exceed 1,000,000");

        RuleFor(x => x.CostPrice)
            .GreaterThanOrEqualTo(0).WithMessage("Cost price cannot be negative")
            .LessThan(1000000).WithMessage("Cost price cannot exceed 1,000,000")
            .When(x => x.CostPrice.HasValue);

        RuleFor(x => x.SupplierId)
            .GreaterThan(0).WithMessage("Supplier is required");

        RuleFor(x => x.CategoryId)
            .GreaterThan(0).WithMessage("Category is required")
            .When(x => !x.CategoryId.IsNullOrEmptyOrDefault());
    }
}
