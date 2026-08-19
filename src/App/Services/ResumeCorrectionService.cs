using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProfessionalHub.ResumeTools.Models;

public class ResumeCorrectionService
{
    public async Task<HashSet<string>> ProcessTermsAsync(List<string> incomingTerms, ResumeFixOptions options)
    {
        // Using case-insensitive comparer aligned with application standards
        var termsToAdd = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (incomingTerms is null || options is null)
        {
            return termsToAdd;
        }

        foreach (var term in incomingTerms)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                continue;
            }

            // Check the option directly from your class
            string processedTerm = options.FixGrammarAndSyntax ? FixSpelling(term) : term.Trim();

            // HashSet.Add returns true if the element was added, false if it was already present
            termsToAdd.Add(processedTerm);
        }

        // Simulate async processing boundary if batch operations/database calls are introduced later
        await Task.CompletedTask;

        return termsToAdd;
    }

    private string FixSpelling(string term)
    {
        // Add basic grammar/spelling normalization rules here if needed
        return term.Trim();
    }
}