using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace BlockchainObserver.Database.Entities
{
    public class LastAddressIndex
    {
        [Key]
        [Required]
        public int Id { get; set; }

        [Required]
        [StringLength(10)]
        public string Currency { get; set; }

        [Required]
        public int Index { get; set; }

        
    }
}
