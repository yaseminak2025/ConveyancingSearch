import type { Listing } from "./api";

export function FirmCard({
  city,
  listing,
}: {
  city: string;
  listing: Listing;
}) {
  const d = listing.details;
  const dial = d.phone?.replace(/[^+\d]/g, "");
  return (
    <article className="firm-card">
      <div className="card-top">
        <span className="city-label">{city}</span>
        {listing.discovery !== "existing" && (
          <span className={"badge " + listing.discovery}>
            {listing.discovery === "new" ? "New" : "Returned"}
          </span>
        )}
      </div>
      <h3>{d.name}</h3>
      <p className="address">{d.address}</p>
      <div className="contact">
        {dial ? (
          <a href={"tel:" + dial}>{d.phone}</a>
        ) : (
          <span>Phone not provided</span>
        )}
        {d.website && (
          <a href={d.website} target="_blank" rel="noopener noreferrer">
            Website ↗
          </a>
        )}
        {d.contactUrl && (
          <a href={d.contactUrl} target="_blank" rel="noopener noreferrer">
            Contact form ↗
          </a>
        )}
      </div>
      <a
        className="source-link"
        href={d.sourceUrl}
        target="_blank"
        rel="noopener noreferrer"
      >
        View source listing <span aria-hidden="true">↗</span>
      </a>
    </article>
  );
}
