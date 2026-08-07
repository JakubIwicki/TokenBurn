export interface GuideSection {
  heading: string;
  paragraphs: string[];
}

export interface Guide {
  slug: string;
  title: string;
  description: string;
  sections: GuideSection[];
}

export const guides: Guide[] = [
  {
    slug: "what-is-tokenburn",
    title: "What is TokenBurn?",
    description:
      "An overview of TokenBurn, the telemetry pipeline that tracks LLM token usage and spend across models and providers.",
    sections: [
      {
        heading: "Tracking spend across many models",
        paragraphs: [
          "Teams that build on language models quickly lose track of what they are spending. Different providers quote prices in different units, apply caching discounts, and bill per run, per message, or per token. TokenBurn is a telemetry pipeline that collects run data from those providers and normalizes it into one view.",
          "Every observed run is attributed to a model and a service, and the token counts and cost are computed from a versioned pricing registry. That keeps the numbers stable even as providers change their rate cards.",
        ],
      },
      {
        heading: "What you see in the directory",
        paragraphs: [
          "The public model directory lists the models TokenBurn has priced, with their context windows and per-million-token prices for input, cache reads, cache writes, and output. The usage tables show how many runs, messages, and tokens each service has recorded for a model, along with the total estimated cost.",
          "Only aggregated, anonymous statistics are published. Individual prompts, responses, and any identifying data stay inside the pipeline.",
        ],
      },
    ],
  },
  {
    slug: "how-llm-token-costs-work",
    title: "How LLM token costs work",
    description:
      "A guide to per-million-token pricing: input, output, and the cheaper cache read and write lanes.",
    sections: [
      {
        heading: "Tokens, not words",
        paragraphs: [
          "Language models bill by tokens, the atomic units of text they read and write. A token is roughly three-quarters of a word in English, so a short conversation can still consume thousands of tokens. Because every provider quotes prices per million tokens, costs scale with how much text actually flows through a model.",
        ],
      },
      {
        heading: "Input, output, and caching",
        paragraphs: [
          "Input tokens are the prompt you send, and output tokens are the completion the model returns. Most providers also offer a cache: text you have already sent can be re-read at a steep discount, and there is a separate, usually higher, price for writing new entries into that cache.",
          "That is why the directory lists four prices per model: input, cache read, cache write, and output. Estimating a real bill means knowing how much of your traffic hits each lane.",
        ],
      },
      {
        heading: "Reading the prices",
        paragraphs: [
          "Every price is quoted per million tokens (MTok). A price of $0.15 / MTok input means 100 million input tokens cost $15 before any discounts. Compare the same lane across models to see which one is cheapest for your mix of prompts and cached traffic.",
        ],
      },
    ],
  },
  {
    slug: "reading-the-model-directory",
    title: "How to read the model directory",
    description:
      "Understanding the per-model pricing cards and usage tables in the TokenBurn model directory.",
    sections: [
      {
        heading: "The pricing card",
        paragraphs: [
          "Each model in the directory has a card showing its slug, its provider, and its context window — the maximum number of tokens a single conversation can hold. Below that are the four per-million-token prices: input, cache read, cache write, and output.",
          "A dash for the context window means the provider does not publish one for that model.",
        ],
      },
      {
        heading: "The usage table",
        paragraphs: [
          "Opening a model shows the recorded usage, broken down by service. Each row reports the number of runs, the number of priced runs, the message count, and the token totals for each billing lane. The cost column is the estimated spend in US dollars for those runs.",
          "A model with no usage yet simply shows an empty usage section — the pricing card still stands on its own.",
        ],
      },
    ],
  },
];
