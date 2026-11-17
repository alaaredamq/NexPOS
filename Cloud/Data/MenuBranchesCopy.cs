namespace Cloud.Data;

/// <summary>
/// Used once by the AddMenuBranchesAndOrderTypes migrations (SQLite and SQL Server): moves a menu's single linked branch, and its POS
/// dishes' products and prices, into MenuBranches and MenuItemBranches. Plain SQL that both databases read.
/// </summary>
internal static class MenuBranchesCopy
{
    public const string Branches = """
        INSERT INTO MenuBranches (MenuId, SiteId, Slug, SortOrder, CatalogHash, CatalogAt, PosArranged)
        SELECT m.Id, m.LinkedSiteId, 'main', 0, m.CatalogHash, m.CatalogAt, m.PosArranged
        FROM Menus m
        WHERE m.LinkedSiteId IS NOT NULL AND EXISTS (SELECT 1 FROM Sites s WHERE s.Id = m.LinkedSiteId);
        """;

    public const string Prices = """
        INSERT INTO MenuItemBranches (MenuItemId, SiteId, PosProductId, Price, Currency)
        SELECT i.Id, m.LinkedSiteId, i.PosProductId, i.Price, i.Currency
        FROM MenuItems i INNER JOIN Menus m ON m.Id = i.MenuId
        WHERE i.PosProductId IS NOT NULL AND m.LinkedSiteId IS NOT NULL AND EXISTS (SELECT 1 FROM Sites s WHERE s.Id = m.LinkedSiteId);
        """;
}
