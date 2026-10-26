import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { SearchPage } from "./search/SearchPage";
import "./styles.css";
const root = document.getElementById("root");
if (!root) throw new Error("Application root missing.");
createRoot(root).render(
  <StrictMode>
    <SearchPage />
  </StrictMode>,
);
