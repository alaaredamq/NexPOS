using System.ComponentModel.DataAnnotations;

namespace Web.Models.Auth;

public class ChangePinViewModel
{
    [Required(ErrorMessage = "New PIN is required")]
    [RegularExpression(@"^\d{4}$", ErrorMessage = "PIN must be exactly 4 digits")]
    [DataType(DataType.Password)]
    [Display(Name = "New PIN")]
    public string NewPin { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please confirm your new PIN")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm PIN")]
    public string ConfirmPin { get; set; } = string.Empty;
}

public class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Current PIN is required")]
    [RegularExpression(@"^\d{4}$", ErrorMessage = "PIN must be exactly 4 digits")]
    [DataType(DataType.Password)]
    [Display(Name = "Current PIN")]
    public string CurrentPin { get; set; } = string.Empty;

    [Required(ErrorMessage = "New PIN is required")]
    [RegularExpression(@"^\d{4}$", ErrorMessage = "PIN must be exactly 4 digits")]
    [DataType(DataType.Password)]
    [Display(Name = "New PIN")]
    public string NewPin { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please confirm your new PIN")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm New PIN")]
    public string ConfirmPin { get; set; } = string.Empty;
}
