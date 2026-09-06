# Supermarket POS System - Entity Design

## Overview
This document outlines the comprehensive entity design for the supermarket POS system, built on the existing `BaseEntity` foundation.

## BaseEntity Foundation
All entities inherit from `BaseEntity` which provides:
- `Id` (Primary Key)
- `CreatedAt` (Audit trail)
- `UpdatedAt` (Audit trail)
- `IsDeleted` (Soft delete support)

## Domain Structure

### 1. Products Domain (`Core.Entities.Products`)

#### Core Entities:
- **Category**: Product categorization (e.g., Beverages, Snacks, Household)
- **SubCategory**: Secondary categorization for better organization
- **Product**: Main product entity with pricing, inventory tracking, and business rules
- **ProductBarcode**: Support for multiple barcodes per product

#### Key Features:
- Hierarchical categorization (Category > SubCategory > Product)
- Multi-barcode support for flexible scanning
- Built-in promotional pricing with date ranges
- Comprehensive inventory tracking settings
- Tax configuration per product
- Brand and manufacturer tracking

### 2. Inventory Domain (`Core.Entities.Inventory`)

#### Core Entities:
- **StockLevel**: Current stock quantities and locations
- **InventoryMovement**: Complete audit trail of all stock movements

#### Key Features:
- Real-time stock tracking with reserved quantities
- Location-based inventory (zones, aisles)
- Complete movement history (purchases, sales, adjustments)
- Average cost tracking
- Support for multiple movement types

### 3. Sales Domain (`Core.Entities.Sales`)

#### Core Entities:
- **Sale**: Main transaction entity
- **SaleItem**: Individual line items within a sale
- **SalePayment**: Payment details with support for split payments
- **Customer**: Customer information and loyalty tracking

#### Key Features:
- Complete transaction lifecycle management
- Multi-payment method support
- Customer loyalty and VIP tracking
- Line-item level pricing and discounts
- Tax calculation per line item
- Transaction voiding capabilities

### 4. Users Domain (`Core.Entities.Users`)

#### Core Entities:
- **User**: System users with role-based access
- **Permission**: Granular permission system
- **UserPermission**: User-specific permission overrides
- **RolePermission**: Role-based permission assignments
- **UserSession**: Session management and security

#### Key Features:
- Role-based access control (RBAC)
- Granular permission system
- Session management with device tracking
- Failed login attempt tracking
- Employee information integration

### 5. Configuration Domain (`Core.Entities.Configuration`)

#### Core Entities:
- **SystemSetting**: Flexible system configuration
- **Company**: Business information and branding
- **TaxRate**: Tax configuration with regional support

#### Key Features:
- Type-safe system settings
- Complete company profile management
- Flexible tax rate configuration
- Receipt customization settings

### 6. Reports Domain (`Core.Entities.Reports`)

#### Core Entities:
- **SalesReport**: Report generation and storage

#### Key Features:
- Asynchronous report processing
- Flexible filtering system
- Multiple report types support
- Report history and storage

## Design Principles

### 1. **Domain-Driven Design (DDD)**
- Clear domain boundaries
- Rich domain models with business logic
- Aggregate roots for consistency

### 2. **Audit Trail**
- All entities inherit audit fields from BaseEntity
- Complete transaction history
- User activity tracking

### 3. **Soft Delete Pattern**
- `IsDeleted` flag prevents data loss
- Historical data preservation
- Referential integrity maintenance

### 4. **Flexible Configuration**
- Settings-driven behavior
- Multi-tenant ready design
- Regional customization support

### 5. **Security First**
- Role-based access control
- Permission-level granularity
- Session management
- Audit logging

## Key Relationships

### Core Business Flow:
1. **Products** are organized in **Categories** and **SubCategories**
2. **Users** process **Sales** containing **SaleItems**
3. **InventoryMovements** track all stock changes
4. **Customers** can be associated with sales for loyalty tracking
5. **Reports** provide business intelligence

### Security Model:
1. **Users** have **Roles** (SuperAdmin, Admin, Manager, Cashier, etc.)
2. **Permissions** are assigned to **Roles** via **RolePermissions**
3. **Users** can have additional **UserPermissions** for overrides
4. **UserSessions** track active sessions and security

## Lebanese Market Specific Features

### Currency Support:
- Dual currency display (LBP/USD)
- Exchange rate management
- Price display in both currencies

### Tax Compliance:
- 11% VAT support
- Tax registration number tracking
- Tax reporting capabilities

### Local Business Practices:
- Customer phone/name capture
- Receipt customization in Arabic/English
- Local payment methods

## Usage Examples

### Creating a Product:
```csharp
var product = new Product
{
    Name = "Aquafina Water 500ml",
    Barcode = "8901036001234",
    SKU = "AQ-500-001",
    SellingPrice = 2000m, // LBP
    CategoryId = beveragesCategoryId,
    TaxRate = 0.11m,
    TrackInventory = true,
    MinStockLevel = 10,
    IsActive = true
};
```

### Processing a Sale:
```csharp
var sale = new Sale
{
    SaleNumber = "POS-2025-001",
    CashierId = userId,
    PaymentMethod = PaymentMethod.Cash,
    Status = SaleStatus.InProgress
};

sale.SaleItems.Add(new SaleItem
{
    ProductId = productId,
    Quantity = 2,
    UnitPrice = 2000m,
    TaxRate = 0.11m
});
```

## Database Considerations

### Indexing Strategy:
- Primary keys on all entities
- Foreign key indexes
- Business key indexes (Barcode, SKU, SaleNumber)
- Date-based indexes for reporting

### Performance Optimization:
- Navigation properties for lazy loading
- Computed properties for calculations
- Efficient query patterns

This design provides a robust, scalable foundation for a modern supermarket POS system while maintaining flexibility for future enhancements.

