using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace PrayerApp.Api.Common;

public static class DbUpdateExceptionExtensions
{
    // SQL Server error numbers: 2627 = PRIMARY KEY / UNIQUE constraint, 2601 = unique index.
    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2627 or 2601 };
}
